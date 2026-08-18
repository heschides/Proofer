using Microsoft.Extensions.Options;

namespace Sati.Api.Infrastructure;

internal sealed class ApiClock(IOptions<SatiApiOptions> options)
{
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);

    public DateTime Today => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone).Date;

    // Agency-local wall clock, including time of day. Anything a person READS as
    // a time — a journal stamp, for one — has to come from here: the host's own
    // local time is UTC in Azure and would present hours off the clock the case
    // manager just looked at. Stored instants stay UTC.
    public DateTime Now => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone).DateTime;

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
