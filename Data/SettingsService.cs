using Microsoft.EntityFrameworkCore;
using Sati.Models;
using Sati.Services;

namespace Sati.Data
{
    public class SettingsService : ISettingsService
    {
        private readonly IDbContextFactory<SatiContext> _contextFactory;
        private readonly ISessionService _sessionService;

        public SettingsService(IDbContextFactory<SatiContext> contextFactory, ISessionService sessionService)
        {
            _contextFactory = contextFactory;
            _sessionService = sessionService;
        }

        public async Task<Settings> LoadAsync()
        {
            await using var context = _contextFactory.CreateDbContext();
            var agencyId = CurrentAgencyId();
            var settings = await context.Settings.SingleOrDefaultAsync(x => x.AgencyId == agencyId);

            if (settings is null)
            {
                settings = new Settings
                {
                    AgencyId = agencyId,
                    ReviewOpenDaysBefore = 10,
                    ReviewDaysAfterDue = 10,
                    PcpOpenDaysBefore = 90,
                    PcpDaysAfterDue = 30,
                    CompAssessmentOpenDaysBefore = 30,
                    CompAssessmentDaysAfterDue = 30,
                    ReclassificationOpenDaysBefore = 15,
                    ReclassificationDaysAfterDue = 0,
                    SafetyPlanOpenDaysBefore = 60,
                    SafetyPlanDaysAfterDue = 30,
                    PrivacyPracticesOpenDaysBefore = 30,
                    PrivacyPracticesDaysAfterDue = 30,
                    ReleaseAgencyOpenDaysBefore = 30,
                    ReleaseAgencyDaysAfterDue = 30,
                    ReleaseDhhsOpenDaysBefore = 30,
                    ReleaseDhhsDaysAfterDue = 30,
                    ReleaseMedicalOpenDaysBefore = 30,
                    ReleaseMedicalDaysAfterDue = 30,
                    // Anniversary offsets (anniversary − N days = due date).
                    // PCP, Safety Plan, Privacy Practices, and Releases are due
                    // on the anniversary itself. Comp Assessment is due 60 days
                    // earlier so the next year's PCP can be informed by it.
                    // Reclassification is due 30 days earlier (Evergreen workflow).
                    Q4RDaysBeforeAnniversary = 5,
                    PcpDaysBeforeAnniversary = 0,
                    CompAssessmentDaysBeforeAnniversary = 60,
                    ReclassificationDaysBeforeAnniversary = 30,
                    SafetyPlanDaysBeforeAnniversary = 0,
                    PrivacyPracticesDaysBeforeAnniversary = 0,
                    ReleaseAgencyDaysBeforeAnniversary = 0,
                    ReleaseDhhsDaysBeforeAnniversary = 0,
                    ReleaseMedicalDaysBeforeAnniversary = 0,
                };

                context.Settings.Add(settings);
                await context.SaveChangesAsync();
            }

            return settings;
        }

        public async Task SaveAsync(Settings settings)
        {
            if (!SettingsAccessPolicy.CanManageAgencySettings(_sessionService.CurrentUser?.Role))
                throw new SettingsSaveException(
                    "Only an agency administrator can change operational settings.",
                    new UnauthorizedAccessException());

            await using var context = _contextFactory.CreateDbContext();
            var agencyId = CurrentAgencyId();
            var tracked = await context.Settings.SingleOrDefaultAsync(x => x.Id == settings.Id && x.AgencyId == agencyId)
                ?? throw new InvalidOperationException("The settings record is outside the current agency.");

            if (tracked.Revision != settings.Revision)
                throw new SettingsConcurrencyException();

            context.Entry(tracked).CurrentValues.SetValues(settings);
            tracked.Id = settings.Id;
            tracked.AgencyId = agencyId;
            tracked.Revision++;
            try
            {
                await context.SaveChangesAsync();
                settings.Revision = tracked.Revision;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                throw new SettingsConcurrencyException(ex);
            }
        }

        private int CurrentAgencyId() => _sessionService.CurrentUser?.AgencyId
            ?? throw new InvalidOperationException("A signed-in user is required to access agency settings.");
    }
}
