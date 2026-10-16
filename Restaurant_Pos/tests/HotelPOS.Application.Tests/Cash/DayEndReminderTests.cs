using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Features.DayEnd;
using HotelPOS.Application.Features.Reports;
using HotelPOS.Application.Tests.Support;
using HotelPOS.Contracts.Admin;
using HotelPOS.Contracts.Realtime;
using HotelPOS.Contracts.Security;
using HotelPOS.Domain.Cash;
using HotelPOS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HotelPOS.Application.Tests.Cash;

public class DayEndReminderTests : DatabaseTestBase
{
    public DayEndReminderTests(DatabaseFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task BeforeTheReminderTime_NothingIsSent()
    {
        var reminder = NewReminder();
        await SetLocalTimeAsync(22, 59);

        (await reminder.RunAsync(CancellationToken.None)).Should().BeFalse();
        Realtime.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task AtTheReminderTime_ManagersAndAdminsAreToldOnce_PerBusinessDay()
    {
        var state = new DayEndReminderState();
        await SetLocalTimeAsync(23, 0);

        (await NewReminder(state).RunAsync(CancellationToken.None)).Should().BeTrue();
        Clock.Advance(TimeSpan.FromMinutes(1));
        (await NewReminder(state).RunAsync(CancellationToken.None)).Should().BeFalse("one reminder per business day");

        var sent = Realtime.Events.Single();
        sent.EventName.Should().Be(HubEvents.DayEndDue);
        sent.Audience.Roles.Should().BeEquivalentTo(new[] { Roles.Admin, Roles.Manager });
        sent.Audience.DeviceIds.Should().BeEmpty();
        var payload = (DayEndDueEvent)sent.Payload;
        payload.ReminderTime.Should().Be("23:00");
        payload.BusinessDay.Should().Be((await Get<IBusinessDays>().GetAsync()).DayOf(Clock.UtcNow.AddMinutes(-1)));

        // The next business day is reminded again.
        Clock.Advance(TimeSpan.FromHours(24));
        (await NewReminder(state).RunAsync(CancellationToken.None)).Should().BeTrue();
        Realtime.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task WhenTheDayIsAlreadyClosed_NothingIsSent()
    {
        await SetLocalTimeAsync(23, 30);
        var day = (await Get<IBusinessDays>().GetAsync()).DayOf(Clock.UtcNow);
        var admin = await AdminIdAsync();
        await QueryAsync(async db =>
        {
            db.BusinessDayCloses.Add(new BusinessDayClose(day, admin, Clock.UtcNow, "[]", "{}", "backup.dump"));
            return await db.SaveChangesAsync();
        });

        (await NewReminder().RunAsync(CancellationToken.None)).Should().BeFalse();
        Realtime.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task TheTimeIsASetting_AndABadValueFallsBackToTheDefault()
    {
        (await QueryAsync(db => db.Settings.CountAsync(s => s.Key == SettingKeys.DayEndReminderTime))).Should().Be(1, "it is seeded");
        await SetSettingAsync("21:30");
        await SetLocalTimeAsync(21, 30);
        (await NewReminder().RunAsync(CancellationToken.None)).Should().BeTrue();
        ((DayEndDueEvent)Realtime.Events.Single().Payload).ReminderTime.Should().Be("21:30");

        Realtime.Reset();
        await SetSettingAsync("late");
        await SetLocalTimeAsync(22, 0);
        (await NewReminder().RunAsync(CancellationToken.None)).Should().BeFalse("the default is 23:00");
    }

    [Fact]
    public async Task ATimeAfterMidnight_BelongsToTheSameBusinessDay()
    {
        await SetSettingAsync("01:00");
        await SetLocalTimeAsync(0, 30);
        var reminder = NewReminder();
        (await reminder.RunAsync(CancellationToken.None)).Should().BeFalse("00:30 is before 01:00 on the business-day clock");

        await SetLocalTimeAsync(1, 0);
        (await reminder.RunAsync(CancellationToken.None)).Should().BeTrue();
    }

    private DayEndReminder NewReminder(DayEndReminderState? state = null) =>
        new(Get<IAppDbContext>(), Get<IBusinessDays>(), Clock, Realtime, state ?? new DayEndReminderState());

    // Moves the test clock to the given local wall time on the day after its start (in the business-day zone).
    private async Task SetLocalTimeAsync(int hour, int minute)
    {
        var zone = (await Get<IBusinessDays>().GetAsync()).Zone;
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(TestClock.Start, zone).Date.AddDays(1);
        Clock.UtcNow = TimeZoneInfo.ConvertTimeToUtc(localStart.AddHours(hour).AddMinutes(minute), zone);
    }

    private Task SetSettingAsync(string value) =>
        QueryAsync(db => db.Settings.Where(s => s.Key == SettingKeys.DayEndReminderTime)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.Value, value)));
}
