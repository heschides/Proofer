using Microsoft.Extensions.Options;

namespace Sati.Api.Infrastructure;

internal sealed class ApiClock(IOptions<SatiApiOptions> options)
{
    private readonly TimeZoneInfo _timeZone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZoneId);

    public DateTime Today => TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone).Date;
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
