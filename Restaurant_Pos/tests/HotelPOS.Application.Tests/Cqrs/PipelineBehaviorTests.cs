using HotelPOS.Application.Common;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Common;
using HotelPOS.Contracts.Floor;
using HotelPOS.Contracts.Security;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application.Tests.Cqrs;

public class PipelineBehaviorTests : DatabaseTestBase
{
    public PipelineBehaviorTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Send_WithoutThePermission_ReturnsForbidden_AndNeverRunsTheHandler()
    {
        var waiter = await CreateUserAsync("waiter9", "Waiter@123", Roles.Waiter);
        CurrentUser.SignIn(waiter.Id, waiter.Username, Roles.Waiter);

        var result = await SendAsync(new ProbeCommand(Section("Terrace")));

        result.Error!.Code.Should().Be(ErrorCodes.Forbidden);
        result.Error.Message.Should().Be("You do not have permission to perform this action.");
        (await SectionCountAsync()).Should().Be(0);
        (await ProbeAuditCountAsync()).Should().Be(0);
        Realtime.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_WithThePermission_RunsTheHandler()
    {
        await SignInAsManagerAsync();

        var result = await SendAsync(new ProbeCommand(Section("Terrace")));

        result.IsSuccess.Should().BeTrue(result.Error?.Message);
    }

    [Fact]
    public async Task Send_WithInvalidInput_ReturnsTheApiValidationShape_AndNeverRunsTheHandler()
    {
        await SignInAsManagerAsync();

        var result = await SendAsync(new ProbeCommand(new CreateSectionRequest { Name = "", SortOrder = -1 }));

        result.Error!.Code.Should().Be(ErrorCodes.ValidationError);
        result.Error.Message.Should().Be("One or more fields are invalid.");
        result.Error.Details.Select(d => d.Field).Should().BeEquivalentTo("name", "sortOrder");
        result.Error.Details.Should().OnlyContain(d => d.Code == ErrorCodes.ValidationError);
        (await SectionCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Send_WhenTheHandlerSucceeds_CommitsTheChange_WithItsAuditRow()
    {
        var managerId = await SignInAsManagerAsync();

        var result = await SendAsync(new ProbeCommand(Section("Terrace")));

        var sectionId = result.Value;
        (await QueryAsync(db => db.Sections.AnyAsync(s => s.Id == sectionId))).Should().BeTrue();
        var audit = (await QueryAsync(db => db.AuditLogs.Where(a => a.Action == AuditActions.SectionCreated).ToListAsync()))
            .Should().ContainSingle().Subject;
        audit.EntityType.Should().Be("Section");
        audit.EntityId.Should().Be(sectionId.ToString());
        audit.UserId.Should().Be(managerId);
        audit.NewValues.Should().Contain("Terrace");
    }

    [Fact]
    public async Task Send_PublishesEventsOnlyAfterTheCommit()
    {
        await SignInAsManagerAsync();

        var result = await SendAsync(new ProbeCommand(Section("Terrace")));

        var published = Realtime.Events.Should().ContainSingle().Subject;
        published.EventName.Should().Be(ProbeCommand.EventName);
        var probe = published.Payload.Should().BeOfType<ProbeEvent>().Subject;
        probe.SectionId.Should().Be(result.Value);
        probe.SectionVisibleToOtherConnections.Should().BeTrue("the event must not go out before other terminals can read the change");
    }

    [Theory]
    [InlineData(ProbeOutcome.Fail)]
    [InlineData(ProbeOutcome.Throw)]
    public async Task Send_WhenTheHandlerFailsAfterSaving_RollsBack_LeavingNoStateNoAuditRowAndNoEvent(ProbeOutcome outcome)
    {
        await SignInAsManagerAsync();

        if (outcome == ProbeOutcome.Throw)
        {
            var act = () => SendAsync(new ProbeCommand(Section("Terrace"), outcome));
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        else
        {
            var result = await SendAsync(new ProbeCommand(Section("Terrace"), outcome));
            result.Error!.Code.Should().Be(ErrorCodes.BusinessRule);
        }

        (await SectionCountAsync()).Should().Be(0);
        (await ProbeAuditCountAsync()).Should().Be(0);
        Realtime.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Send_AfterARolledBackCommandInTheSameScope_DoesNotResurrectItsChanges()
    {
        await SignInAsManagerAsync();
        await using var scope = Fixture.Services.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        await sender.Send(new ProbeCommand(Section("Rolled back"), ProbeOutcome.Fail));
        var kept = await sender.Send(new ProbeCommand(Section("Kept")));

        kept.IsSuccess.Should().BeTrue(kept.Error?.Message);
        (await QueryAsync(db => db.Sections.Select(s => s.Name).ToListAsync())).Should().Equal("Kept");
        (await ProbeAuditCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Send_ACommandThatKeepsItsFailure_CommitsTheChange_AndTheAuditRow()
    {
        var result = await SendAsync(new KeepOnFailureProbe(Section("Terrace")));

        result.Error!.Code.Should().Be(ErrorCodes.InvalidCredentials);
        (await SectionCountAsync()).Should().Be(1);
        (await ProbeAuditCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Send_ACommandRecordingAuditEntriesWithoutDeclaringIt_FailsAndRollsBack()
    {
        var act = () => SendAsync(new UndeclaredAuditProbe(Section("Terrace")));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*IAuditedCommand*");
        (await SectionCountAsync()).Should().Be(0);
    }

    private static CreateSectionRequest Section(string name) => new() { Name = name, SortOrder = 1 };

    private async Task<int> SignInAsManagerAsync()
    {
        var manager = await CreateUserAsync("manager9", "Manager@123", Roles.Manager);
        CurrentUser.SignIn(manager.Id, manager.Username, Roles.Manager);
        return manager.Id;
    }

    private Task<int> SectionCountAsync() => QueryAsync(db => db.Sections.CountAsync());

    private Task<int> ProbeAuditCountAsync() =>
        QueryAsync(db => db.AuditLogs.CountAsync(a => a.Action == AuditActions.SectionCreated));
}
