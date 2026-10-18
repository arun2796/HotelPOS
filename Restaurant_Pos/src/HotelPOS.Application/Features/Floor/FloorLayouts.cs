using HotelPOS.Application.Common.Interfaces;
using HotelPOS.Application.Common.Results;
using HotelPOS.Contracts.Floor;
using Microsoft.EntityFrameworkCore;

namespace HotelPOS.Application.Features.Floor;

internal static class FloorLayouts
{
    // Active tables of one section (placed or not), ordered by code so unplaced tables list predictably.
    public static async Task<Result<FloorLayoutDto>> LoadAsync(IAppDbContext db, int sectionId, CancellationToken cancellationToken)
    {
        var section = await db.Sections.AsNoTracking()
            .Where(s => s.Id == sectionId)
            .Select(s => new { s.Id, s.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (section is null)
        {
            return AppErrors.NotFound("Section", sectionId);
        }

        var tables = await db.Tables.AsNoTracking()
            .Where(t => t.SectionId == sectionId && t.IsActive)
            .OrderBy(t => t.Code)
            .Select(t => new FloorTableDto
            {
                TableId = t.Id,
                Code = t.Code,
                Capacity = t.Capacity,
                Status = t.Status,
                PosX = t.PosX,
                PosY = t.PosY,
                Width = t.Width,
                Height = t.Height,
                Shape = t.Shape,
                Rotation = t.Rotation,
                HasLayout = t.PosX != null,
            })
            .ToListAsync(cancellationToken);

        return new FloorLayoutDto { SectionId = section.Id, SectionName = section.Name, Tables = tables };
    }
}
