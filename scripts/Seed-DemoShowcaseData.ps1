param(
    [string]$SqlServer = "(localdb)\MSSQLLocalDB",
    [string]$Database = "SatiDemo"
)

$ErrorActionPreference = "Stop"
$seedTag = "DEMO_SHOWCASE_V1"
$today = [DateTime]::Today

if ($Database -cne "SatiDemo") {
    throw "This seed is hard-limited to the SatiDemo database."
}

function Add-CommandParameters($Command, [hashtable]$Parameters) {
    foreach ($entry in $Parameters.GetEnumerator()) {
        $value = if ($null -eq $entry.Value) { [DBNull]::Value } else { $entry.Value }
        [void]$Command.Parameters.AddWithValue("@$($entry.Key)", $value)
    }
}

function Invoke-SeedNonQuery([string]$Sql, [hashtable]$Parameters = @{}) {
    $command = $connection.CreateCommand()
    $command.Transaction = $transaction
    $command.CommandTimeout = 120
    $command.CommandText = $Sql
    Add-CommandParameters $command $Parameters
    return $command.ExecuteNonQuery()
}

function Invoke-SeedScalar([string]$Sql, [hashtable]$Parameters = @{}) {
    $command = $connection.CreateCommand()
    $command.Transaction = $transaction
    $command.CommandTimeout = 120
    $command.CommandText = $Sql
    Add-CommandParameters $command $Parameters
    return $command.ExecuteScalar()
}

function Get-ShowTheme([int]$PersonId) {
    if ($PersonId -ge 1008 -and $PersonId -le 1026) {
        return @{ Show = "Community"; Location = "Study Room F at Greendale"; Distraction = "The meeting remained productive despite an unexpected debate about whether this counted as a bottle episode"; Skill = "planning a chosen community outing without turning it into a campus-wide competition" }
    }
    if ($PersonId -ge 1207 -and $PersonId -le 1221) {
        return @{ Show = "House"; Location = "a quiet conference room at Princeton-Plainsboro"; Distraction = "The whiteboard was reserved for goals, not a differential diagnosis"; Skill = "asking direct questions and choosing among clearly explained options" }
    }
    if ($PersonId -ge 1222 -and $PersonId -le 1236) {
        return @{ Show = "Cheers"; Location = "a familiar neighborhood gathering place"; Distraction = "Everyone knew the participant's name, but the attendance sheet was completed anyway"; Skill = "budgeting for a social outing and keeping the receipt" }
    }
    if ($PersonId -ge 1237 -and $PersonId -le 1251) {
        return @{ Show = "Night Court"; Location = "the community center's evening session"; Distraction = "The discussion adjourned on time and nobody was held in contempt"; Skill = "using a written schedule for evening appointments" }
    }
    if ($PersonId -ge 1252 -and $PersonId -le 1266) {
        return @{ Show = "Who's the Boss?"; Location = "the household planning table"; Distraction = "Responsibility for the chore chart was settled without consulting the laugh track"; Skill = "dividing household tasks and checking them off independently" }
    }
    if ($PersonId -ge 1267 -and $PersonId -le 1281) {
        return @{ Show = "The Cosby Show"; Location = "the Brooklyn Heights community room"; Distraction = "Several opinions and one spectacular sweater attended the meeting"; Skill = "preparing a simple meal and cleaning the workspace afterward" }
    }
    if ($PersonId -ge 1282 -and $PersonId -le 1296) {
        return @{ Show = "Slings & Arrows"; Location = "the New Burbage rehearsal hall"; Distraction = "The plan was reviewed without a ghost, soliloquy, or last-minute casting change"; Skill = "following a backstage checklist and requesting help before opening night" }
    }
    if ($PersonId -ge 1297 -and $PersonId -le 1311) {
        return @{ Show = "Barry"; Location = "a North Hollywood acting workshop"; Distraction = "Objectives were stated plainly and no monologue exceeded two minutes"; Skill = "pausing before decisions and identifying a safe, ordinary next step" }
    }
    if ($PersonId -ge 1312 -and $PersonId -le 1326) {
        return @{ Show = "The Drew Carey Show"; Location = "the Winfred-Louder break room"; Distraction = "Office-supply planning did not become a musical number"; Skill = "comparison shopping and staying within a written budget" }
    }
    if ($PersonId -ge 1327 -and $PersonId -le 1341) {
        return @{ Show = "Scrubs"; Location = "a quiet room near Sacred Heart"; Distraction = "The participant redirected one internal monologue and returned to the agenda"; Skill = "writing down questions before an appointment" }
    }
    if ($PersonId -ge 1342 -and $PersonId -le 1356) {
        return @{ Show = "Family Ties"; Location = "the Keaton family kitchen"; Distraction = "The budget conversation included fewer economic forecasts than expected"; Skill = "balancing personal preferences with a weekly spending plan" }
    }
    return @{ Show = "Superhero crossover"; Location = "the Hall of Justice community room"; Distraction = "Capes were optional and the agenda survived intact"; Skill = "choosing supports without delegating the decision to a mysterious masked vigilante" }
}

function New-AssessmentAnswer(
    [string]$Narrative,
    [int]$Supports = 0,
    [string]$SupportDetails = ""
) {
    return [ordered]@{
        status = 0
        narrative = $Narrative
        supports = $Supports
        supportDetails = $SupportDetails
        exceptionReason = ""
        dissentingOpinion = ""
        yesNoResponse = $null
        followUpYesNoResponse = $null
        details = ""
        therapySessionFormat = 0
        wantsOtherSessionFormat = $null
        wantsFrequencyChange = $null
        therapyFrequencyDirection = 0
        activitySupportLevels = @{}
        activitySkillsTraining = @{}
    }
}

function Get-WaiverLabel($Person) {
    switch ([int]$Person.Waiver) {
        1 { return "Section 21" }
        2 { return "Section 29" }
        default { return "no waiver" }
    }
}

function Get-ServiceSummary($Person) {
    $services = [System.Collections.Generic.List[string]]::new()
    if ([bool]$Person.HasHomeSupport) { $services.Add("agency home support") }
    if ([bool]$Person.HasSelfDirectedHomeSupport) { $services.Add("self-directed home support") }
    if ([bool]$Person.HasSharedLiving) { $services.Add("shared living") }
    if ([bool]$Person.HasCommunitySupport1To1) { $services.Add("one-to-one community support") }
    if ([bool]$Person.HasCommunitySupportSelfDirected) { $services.Add("self-directed community support") }
    if ([bool]$Person.HasCommunitySupportDayProgram) { $services.Add("community support day program") }
    if ([bool]$Person.HasEmploymentSpecialist) { $services.Add("employment specialist services") }
    if ([bool]$Person.HasWorkSupports) { $services.Add("work supports") }

    $waiver = Get-WaiverLabel $Person
    if ($services.Count -eq 0) {
        return "The demographic record lists $waiver enrollment and does not currently identify an individual waiver-service flag. The case manager will verify the current authorization rather than infer a service from waiver enrollment alone."
    }
    return "The demographic record lists $waiver enrollment and the following active supports: $($services -join ', ')."
}

function Get-DiagnosisLabel([string]$Code) {
    switch ($Code) {
        "F32.1" { return "major depressive disorder, single episode, moderate (F32.1)" }
        "F33.0" { return "major depressive disorder, recurrent, mild (F33.0)" }
        "F41.1" { return "generalized anxiety disorder (F41.1)" }
        "F43.1" { return "post-traumatic stress disorder (F43.1)" }
        "F60.3" { return "borderline personality disorder (F60.3)" }
        default { return "" }
    }
}

function Get-AssessmentProfile($Person, $Theme) {
    $bio = if ($Person.Bio -is [DBNull]) { "" } else { [string]$Person.Bio }
    $diagnosisCode = if ($Person.DiagnosisCode -is [DBNull]) { "" } else { [string]$Person.DiagnosisCode }
    $diagnosis = Get-DiagnosisLabel $diagnosisCode
    $isHistorical = $bio -match '(?i)\bdeceased\b|historical reference'
    $identityNeedsReview = $bio -match '(?i)records incomplete|last name unknown|possible duplicate|misfiled|unclear if client|not a client|tangential referral|incidental referral'
    $usesWrittenCommunication = $bio -match '(?i)written communication|notecards|documents|documentation|filing systems|regulations|legal background|attorney|organized|keeps better records'
    $usesExtraProcessingTime = $bio -match '(?i)quiet|private|evasive|rapport-building|communication barriers|language supports|immigrant|emigrant|agrees with whatever|reluctant|resistant'
    $usesStructuredCommunication = $bio -match '(?i)structured|routine|clear expectations|ambiguity|anxiety|catastroph|unpredictable|erratic|fragile|spiral|stress|follow-through|easily influenced'
    $highVerbal = $bio -match '(?i)verbose|highly verbal|talkative|argue|questions every|analyze|reviews all|cite them|opinions'
    $lowSupport = $bio -match '(?i)self-sufficient|independent|high-functioning|low support needs|quietly capable|highly competent|reliable|resourceful|strong work ethic'
    $healthOrBehavioralConcern = $diagnosis -ne "" -or $bio -match '(?i)chronic pain|recovering alcoholic|drug deal|clinically depressed|complex trauma|cognitive concerns|crisis calls|emotional|spiral|stress-related|anxiety'
    $wantsTherapy = $diagnosis -ne "" -or $bio -match '(?i)depressed|trauma|anxiety|crisis calls|emotional|spiral|stress-related'

    $communication = if ($isHistorical) {
        "This is a historical file; no current communication preference can be confirmed. Any future use requires identity and status verification."
    } elseif ($bio -match '(?i)language supports|communication barriers|immigrant|emigrant') {
        "Use plain language, confirm the preferred language, offer a qualified interpreter, and use teach-back rather than assuming agreement."
    } elseif ($usesWrittenCommunication) {
        "Provide the agenda and choices in writing, allow time for review, and end with a written summary of decisions and responsibilities."
    } elseif ($highVerbal) {
        "Allow a complete explanation, use a visible agenda to keep the discussion focused, and summarize the decision without dismissing detail."
    } elseif ($usesExtraProcessingTime) {
        "Use one question at a time, allow quiet processing time, and offer a later follow-up instead of treating silence or hesitation as agreement."
    } elseif ($usesStructuredCommunication) {
        "Use predictable pacing, concrete choices, one speaker at a time, and a brief written next step."
    } else {
        "Use direct language, two or three concrete choices, adequate response time, and a brief summary that can be corrected."
    }

    $strength = if ($bio -match '(?i)resourceful|problem-solving|creative solutions') {
        "resourcefulness and practical problem-solving"
    } elseif ($bio -match '(?i)organized|documents|records|notecards|reliable attendee') {
        "organization, preparation, and reliable follow-through"
    } elseif ($bio -match '(?i)advocat|knows his rights|knows her rights|self-advocacy|questions every|reviews all') {
        "self-advocacy and careful review of decisions"
    } elseif ($bio -match '(?i)community|family|social supports|friend|warm|loyal|beloved') {
        "relationship-building and commitment to important people"
    } elseif ($bio -match '(?i)work ethic|vocational|employee|manager|professional|academic|graduate|physician|nurse|attorney') {
        "work and learning experience, persistence, and a strong sense of responsibility"
    } elseif ($bio -match '(?i)creative|artist|actress|artistic|humor|charming') {
        "creativity, humor, and an ability to engage other people"
    } else {
        "persistence, familiarity with personal routines, and the ability to state preferences when given enough time"
    }

    $risk = if ($isHistorical) {
        "The record identifies this as a historical file. The immediate risk is inappropriate use of an inactive record; staff must verify status before any service or outreach."
    } elseif ($identityNeedsReview) {
        "The seeded profile identifies incomplete or uncertain identity information. The primary current risk is acting on the wrong record; verify legal name, role, and eligibility before relying on this plan."
    } elseif ($bio -match '(?i)recovering alcoholic|drug deal|substance') {
        "Exposure to substance-use settings and stress may increase relapse risk. The safeguard is a person-chosen exit plan, contact with a trusted support, and access to recovery resources."
    } elseif ($bio -match '(?i)chronic pain') {
        "Pain flares may reduce concentration, mobility, and tolerance for long meetings. Build in breaks, confirm medication questions with healthcare providers, and use a backup transportation plan."
    } elseif (($bio -match '(?i)catastroph|crisis calls|anxiety|fragile|emotional|spiral|depressed|trauma|stress') -or ($diagnosis -ne "")) {
        "Unexpected change or interpersonal stress may lead to emotional escalation or withdrawal. Early notice, a quiet break, a trusted contact, and a clear crisis-escalation pathway reduce risk."
    } elseif ($bio -match '(?i)easily influenced|defers to authority|agrees with whatever|easily led') {
        "The person may agree before fully considering personal preference. Supporters should meet without peer pressure, use teach-back, and revisit consequential decisions after reflection time."
    } elseif ($bio -match '(?i)isolated|limited social circle|peripheral|involvement inconsistent') {
        "Limited or inconsistent connection may increase isolation and missed follow-up. Schedule contact by the person's preferred method and identify a backup contact with permission."
    } elseif ($bio -match '(?i)long travel|transport') {
        "Long travel time creates a foreseeable risk of missed appointments. Confirm transportation in advance and offer a remote or rescheduled option when travel is disrupted."
    } elseif ($bio -match '(?i)goals shift|inconsistent follow-through|no discernible plan|no identified goals|difficult to keep seated') {
        "Multi-step plans may lose momentum when priorities or attention shift. Use one short next step, a chosen reminder, and a scheduled review before adding another task."
    } else {
        "No immediate health or safety event is documented. The foreseeable planning risk is a rushed decision when a familiar plan changes; provide notice, a backup option, and time to ask questions."
    }

    $healthSummary = if ($isHistorical) {
        "No current health status can be established from this historical file."
    } elseif ($diagnosis -ne "") {
        "The demographic record lists $diagnosis. This assessment records the diagnosis without adding symptoms, treatment, or medication information that is not in the source record."
    } elseif ($bio -match '(?i)chronic pain') {
        "The seeded profile identifies chronic pain management as an active concern; comfort, mobility, and access to care should be reviewed at each planning contact."
    } elseif ($healthOrBehavioralConcern) {
        "The seeded profile identifies a behavioral-health or wellness concern that warrants routine monitoring and confirmation of the current care plan."
    } else {
        "No coded diagnosis, primary-care provider, or active health concern is recorded in the seeded demographics. Current providers and preventive-care needs should be confirmed."
    }

    $employment = if ([bool]$Person.IsEmployed) {
        "The demographic record identifies current employment. The next step is to confirm job satisfaction, schedule, accommodations, benefits questions, and whether existing supports remain wanted."
    } elseif ([bool]$Person.HasEmploymentSpecialist -or [bool]$Person.HasWorkSupports -or [bool]$Person.OpenWithVR) {
        "Employment-related support is recorded. The person wants the next planning contact to clarify the chosen work goal, provider responsibility, accommodations, and a measurable next step."
    } elseif ($bio -match '(?i)employment|vocational|career|work|employee|manager|professional|academic|student') {
        "The seeded profile identifies work or learning experience as relevant. Explore a person-chosen role that uses $strength and document one low-pressure next step."
    } else {
        "No employment service or current job is recorded. Meaningful activity should be explored without presuming that paid employment is or is not the person's preference."
    }

    $primaryNeedType = if ($isHistorical -or $identityNeedsReview) { 7 } elseif ($healthOrBehavioralConcern) { 4 } else { 2 }
    $primaryNeed = if ($isHistorical -or $identityNeedsReview) {
        "Verify identity, active status, eligibility, and the accuracy of the demographic record before using it for planning."
    } elseif ($healthOrBehavioralConcern) {
        "Confirm the current healthcare or behavioral-health plan, preferred supports, early warning signs, and who may be contacted with consent."
    } else {
        "Practice the chosen multi-step routine: $($Theme.Skill)."
    }
    $primaryResult = if ($isHistorical -or $identityNeedsReview) {
        "The record is either corrected and activated with verified information or clearly marked historical/inactive before the next planning action."
    } elseif ($healthOrBehavioralConcern) {
        "The plan names the person's preferred preventive supports and a clear follow-up responsibility without adding unverified clinical information."
    } else {
        "$($Person.FirstName) completes the chosen routine with no more than one requested prompt on two consecutive opportunities."
    }

    return [ordered]@{
        Bio = $bio
        Diagnosis = $diagnosis
        Historical = $isHistorical
        IdentityNeedsReview = $identityNeedsReview
        Communication = $communication
        Strength = $strength
        Risk = $risk
        HealthSummary = $healthSummary
        HealthConcern = $healthOrBehavioralConcern
        WantsTherapy = $wantsTherapy
        Employment = $employment
        ServiceSummary = Get-ServiceSummary $Person
        LowSupport = $lowSupport
        StructuredSupport = $usesStructuredCommunication -or $usesExtraProcessingTime
        CommunicationAssessment = $bio -match '(?i)communicates primarily|communication barriers|language supports'
        PrimaryNeedType = $primaryNeedType
        PrimaryNeed = $primaryNeed
        PrimaryResult = $primaryResult
    }
}

function New-AssessmentDocument(
    $Person,
    $Theme,
    [int]$ProviderId,
    [string]$ProviderName,
    [bool]$AssociateProvider
) {
    $name = "$($Person.FirstName) $($Person.LastName)".Trim()
    $profile = Get-AssessmentProfile $Person $Theme
    $supportMethod = if ($profile.LowSupport) { 32 } elseif ($profile.StructuredSupport) { 3 } else { 2 }
    $supportDetails = if ($profile.LowSupport) { "" } else { $profile.Communication }
    $answers = [ordered]@{}
    $answers["self-view"] = New-AssessmentAnswer "$name identifies personal choice, being treated as an adult, and building on $($profile.Strength) as important. The seeded profile was used as background, not as a substitute for the person's voice."
    $answers["what-people-like"] = New-AssessmentAnswer "People who know $($Person.FirstName) describe practical strengths in $($profile.Strength). Planning should use those strengths in observable ways, such as preparing for a meeting, choosing the next step, or helping refine a routine."
    $answers["good-life"] = New-AssessmentAnswer "A good life includes a stable home base, contact with chosen people, control over the daily schedule, and regular access to $($Theme.Location). A current goal is $($Theme.Skill)."
    $answers["communication-overview"] = New-AssessmentAnswer $profile.Communication
    $answers["receptive-communication"] = New-AssessmentAnswer "For important information, $($profile.Communication) Understanding is confirmed by asking $($Person.FirstName) to explain the choice and next step in their own words."
    $answers["expressive-communication"] = New-AssessmentAnswer "$($Person.FirstName) communicates through speech, tone, facial expression, questions, and changes in participation. Supporters should pause when the person says no, becomes quieter, changes the subject, or asks to revisit the issue, and should confirm meaning without pressure."
    $answers["communication-assessment-received"] = New-AssessmentAnswer ""
    $answers["communication-assessment-received"].yesNoResponse = $false
    $answers["communication-assessment-schedule"] = New-AssessmentAnswer ""
    $answers["communication-assessment-schedule"].yesNoResponse = [bool]$profile.CommunicationAssessment
    $answers["home-independent"] = New-AssessmentAnswer "$($Person.FirstName) chooses clothing, manages preferred belongings, and completes familiar parts of the morning routine independently. The team should verify each activity rather than generalize independence from the profile summary."
    $answers["home-supports"] = New-AssessmentAnswer "$($profile.ServiceSummary) For multi-step home tasks, begin with setup and one prompt, preserve privacy, and increase assistance only when the person requests it or an observed need is documented."
    $answers["home-support-levels"] = New-AssessmentAnswer ""
    $activityLevels = [ordered]@{}
    $activityTraining = [ordered]@{}
    foreach ($activity in @("Meal preparation", "Eating and drinking", "Household cleaning", "Laundry", "Shopping and errands", "Personal hygiene", "Dressing", "Toileting", "Medication routines", "Mobility and transfers")) {
        $activityLevels[$activity] = if ($profile.LowSupport) { 0 } elseif ($activity -in @("Meal preparation", "Household cleaning", "Laundry", "Shopping and errands", "Medication routines")) { 1 } else { 0 }
        if ($profile.Bio -match '(?i)chronic pain' -and $activity -eq "Mobility and transfers") { $activityLevels[$activity] = 3 }
        $activityTraining[$activity] = -not $profile.LowSupport -and $activity -in @("Meal preparation", "Laundry", "Shopping and errands")
    }
    $answers["home-support-levels"].activitySupportLevels = $activityLevels
    $answers["home-support-levels"].activitySkillsTraining = $activityTraining
    $answers["physical-health-view"] = New-AssessmentAnswer "$($profile.HealthSummary) $($Person.FirstName) wants health information explained in plain language and enough time to ask questions."
    $answers["physical-health-change"] = New-AssessmentAnswer "$($Person.FirstName) wants predictable access to preventive care, sleep and movement routines chosen for personal benefit, and follow-up responsibilities written down. No additional health goal is inferred from diagnosis or waiver status."
    $answers["health-concerns"] = New-AssessmentAnswer ""
    $answers["health-concerns"].yesNoResponse = [bool]$profile.HealthConcern
    $answers["health-concerns"].details = if ($profile.HealthConcern) { $profile.HealthSummary } else { "" }
    $answers["mental-health-view"] = New-AssessmentAnswer "$($Person.FirstName) reports that predictable plans, quiet time, humor, and contact with a trusted person are useful when stress increases. $($profile.HealthSummary)"
    $answers["therapy"] = New-AssessmentAnswer ""
    $answers["therapy"].yesNoResponse = $false
    $answers["therapy"].followUpYesNoResponse = [bool]$profile.WantsTherapy
    $answers["risks"] = New-AssessmentAnswer $profile.Risk
    $answers["rights"] = New-AssessmentAnswer "No standing rights restriction is documented. $($Person.FirstName) retains privacy, access to personal belongings and records, control over ordinary choices, and the right to decline optional activities. Any future restriction requires an individualized rationale, consent or authority, a review date, and a less-restrictive plan."
    $answers["community-access"] = New-AssessmentAnswer "$($Person.FirstName) chooses destinations and timing in advance. The seeded case notes identify transportation, scheduling, and $($Theme.Skill) as current planning topics. The next outing will include a confirmed ride and one backup option." $supportMethod $supportDetails
    $answers["relationships"] = New-AssessmentAnswer "$($Person.FirstName) values people who listen, keep commitments, and respect boundaries. Contact should occur by the person's preferred method; paid supporters should help maintain chosen unpaid relationships without replacing them."
    $answers["employment-learning"] = New-AssessmentAnswer $profile.Employment $supportMethod $supportDetails
    $answers["decision-support"] = New-AssessmentAnswer "$($Person.FirstName) makes final day-to-day choices after reviewing two or three concrete options. For consequential decisions, supporters explain likely outcomes, check understanding, allow reflection time, and record the person's own decision. No guardian is recorded in the seeded demographics." $supportMethod $supportDetails
    $answers["dissent"] = New-AssessmentAnswer "$($Person.FirstName) may say no directly, become quieter, or ask to revisit the topic. Supporters should stop, confirm the concern, and offer another time."
    $answers["strengths"] = New-AssessmentAnswer "Planning should build on $($profile.Strength), familiar routines, the ability to make choices, and the concrete practice already documented in case notes for $($Theme.Skill)."
    $answers["priority-needs"] = New-AssessmentAnswer "Priorities are reliable transportation, verification of current authorized services, and $($profile.PrimaryNeed) The case manager will review progress and unresolved record questions during the next planning contact."

    $contributors = @(
        [ordered]@{ id = [Guid]::NewGuid(); name = $name; relationship = "Self" },
        [ordered]@{ id = [Guid]::NewGuid(); name = [string]$Person.CaseManagerName; relationship = "Case manager and assessment author" }
    )
    if ($AssociateProvider) {
        $contributors += [ordered]@{ id = [Guid]::NewGuid(); name = "Casey Coordinator"; relationship = "Community support coordinator at $ProviderName" }
    }

    return [ordered]@{
        contributors = $contributors
        answers = $answers
        needs = @(
            [ordered]@{
                id = [Guid]::NewGuid()
                type = 3
                description = "Reliable transportation and scheduling support for chosen community activities."
                desiredResult = "$($Person.FirstName) attends two chosen activities each month with fewer last-minute changes."
                associateProvider = $AssociateProvider
                providerId = if ($AssociateProvider) { $ProviderId } else { $null }
                providerNameSnapshot = if ($AssociateProvider) { $ProviderName } else { "" }
            },
            [ordered]@{
                id = [Guid]::NewGuid()
                type = $profile.PrimaryNeedType
                description = $profile.PrimaryNeed
                desiredResult = $profile.PrimaryResult
                associateProvider = $false
                providerId = $null
                providerNameSnapshot = ""
            },
            [ordered]@{
                id = [Guid]::NewGuid()
                type = 7
                description = "Confirm current waiver-service authorizations because the assessment must not infer services from waiver enrollment or themed case notes."
                desiredResult = "The next review either records the active service flags and responsible providers or documents that no additional waiver service is currently authorized."
                associateProvider = $false
                providerId = $null
                providerNameSnapshot = ""
            }
        )
    }
}

$connectionString = "Server=$SqlServer;Database=$Database;Integrated Security=true;Encrypt=false;Connect Timeout=30;"
$connection = [System.Data.SqlClient.SqlConnection]::new($connectionString)
$connection.Open()

$identityCommand = $connection.CreateCommand()
$identityCommand.CommandText = "SELECT DB_NAME(), EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id = 1;"
$identityReader = $identityCommand.ExecuteReader()
if (-not $identityReader.Read()) {
    $identityReader.Close()
    $connection.Dispose()
    throw "The Demo identity marker is missing. Nothing was changed."
}
$actualDatabase = $identityReader.GetString(0)
$actualEnvironment = $identityReader.GetString(1)
$identityReader.Close()
if ($actualDatabase -cne "SatiDemo" -or $actualEnvironment -cne "Demo") {
    $connection.Dispose()
    throw "Refusing to seed database '$actualDatabase' marked '$actualEnvironment'. Nothing was changed."
}

$transaction = $connection.BeginTransaction()
try {
    $providers = @(
        @{ Type="Waiver"; Name="Greendale Human Being Services"; Street="1 Community College Way"; City="Greendale"; State="ME"; Zip="04001"; Contact="Dean Pelton"; Phone="207-555-0101"; Services=15; Pass=$true; Eis="Greendale Purchasing - Room 101"; Program="Annie Edison - annie@greendale.demo"; Billing="Abed Nadir - billing@greendale.demo" },
        @{ Type="Healthcare"; Name="Princeton-Plainsboro Community Health"; Street="221 Diagnostic Drive"; City="Portland"; State="ME"; Zip="04101"; Contact="Lisa Cuddy"; Phone="207-555-0102"; Services=0; Pass=$false; Eis=$null; Program=$null; Billing=$null },
        @{ Type="Waiver"; Name="Cheers Community Connections"; Street="84 Beacon Street"; City="Bangor"; State="ME"; Zip="04401"; Contact="Sam Malone"; Phone="207-555-0103"; Services=10; Pass=$false; Eis=$null; Program=$null; Billing=$null },
        @{ Type="Waiver"; Name="Night Court Evening Supports"; Street="100 Centre Street"; City="Augusta"; State="ME"; Zip="04330"; Contact="Harry Stone"; Phone="207-555-0104"; Services=3; Pass=$false; Eis=$null; Program=$null; Billing=$null },
        @{ Type="Waiver"; Name="Bower Household Independence"; Street="12 Fairfield Lane"; City="Brunswick"; State="ME"; Zip="04011"; Contact="Angela Bower"; Phone="207-555-0105"; Services=7; Pass=$false; Eis=$null; Program=$null; Billing=$null },
        @{ Type="Waiver"; Name="Brooklyn Heights Family Supports"; Street="10 Stigwood Avenue"; City="Lewiston"; State="ME"; Zip="04240"; Contact="Clair Huxtable"; Phone="207-555-0106"; Services=11; Pass=$false; Eis=$null; Program=$null; Billing=$null },
        @{ Type="Other"; Name="New Burbage Arts Access"; Street="2 Ghost Light Plaza"; City="Rockland"; State="ME"; Zip="04841"; Contact="Geoffrey Tennant"; Phone="207-555-0107"; Services=8; Pass=$false; Eis=$null; Program=$null; Billing=$null },
        @{ Type="Waiver"; Name="North Hollywood Community Supports"; Street="50 Scene Study Road"; City="Biddeford"; State="ME"; Zip="04005"; Contact="NoHo Hank"; Phone="207-555-0108"; Services=6; Pass=$false; Eis=$null; Program=$null; Billing=$null },
        @{ Type="Other"; Name="Winfred-Louder Community Access"; Street="900 Retail Circle"; City="South Portland"; State="ME"; Zip="04106"; Contact="Drew Carey"; Phone="207-555-0109"; Services=8; Pass=$false; Eis=$null; Program=$null; Billing=$null },
        @{ Type="Healthcare"; Name="Sacred Heart Skills and Wellness"; Street="1 Medicine Avenue"; City="Waterville"; State="ME"; Zip="04901"; Contact="Carla Espinosa"; Phone="207-555-0110"; Services=1; Pass=$true; Eis="Sacred Heart AT Desk"; Program="John Dorian - jd@sacredheart.demo"; Billing="Ted Buckland - ted@sacredheart.demo" },
        @{ Type="Waiver"; Name="Keaton Family Futures"; Street="1980 Sycamore Road"; City="Orono"; State="ME"; Zip="04473"; Contact="Elyse Keaton"; Phone="207-555-0111"; Services=7; Pass=$false; Eis=$null; Program=$null; Billing=$null },
        @{ Type="Other"; Name="Wayne Enterprises Adaptive Technology"; Street="1007 Mountain Drive"; City="Gotham"; State="ME"; Zip="04002"; Contact="Lucius Fox"; Phone="207-555-0112"; Services=0; Pass=$true; Eis="Wayne Applied Sciences"; Program="Barbara Gordon - bgordon@wayne.demo"; Billing="Alfred Pennyworth - accounts@wayne.demo" },
        @{ Type="Waiver"; Name="Xavier Institute Community Living"; Street="1407 Graymalkin Lane"; City="Bar Harbor"; State="ME"; Zip="04609"; Contact="Ororo Munroe"; Phone="207-555-0113"; Services=15; Pass=$false; Eis=$null; Program=$null; Billing=$null },
        @{ Type="Other"; Name="Stark Accessible Futures"; Street="10880 Malibu Point"; City="Kittery"; State="ME"; Zip="03904"; Contact="Pepper Potts"; Phone="207-555-0114"; Services=0; Pass=$true; Eis="Stark Community Projects"; Program="Peter Parker - peter@stark.demo"; Billing="Happy Hogan - happy@stark.demo" },
        @{ Type="Waiver"; Name="Wakanda Outreach and Mobility"; Street="1 Vibranium Way"; City="Presque Isle"; State="ME"; Zip="04769"; Contact="Shuri"; Phone="207-555-0115"; Services=15; Pass=$true; Eis="Wakanda Outreach North"; Program="Shuri - shuri@wakanda.demo"; Billing="Okoye - okoye@wakanda.demo" },
        @{ Type="Other"; Name="Daily Planet Community Supports"; Street="1938 Press Plaza"; City="Portland"; State="ME"; Zip="04101"; Contact="Lois Lane"; Phone="207-555-0116"; Services=8; Pass=$false; Eis=$null; Program=$null; Billing=$null },
        @{ Type="Other"; Name="Nelson and Murdock Advocacy Network"; Street="410 Legal Aid Lane"; City="Bangor"; State="ME"; Zip="04401"; Contact="Foggy Nelson"; Phone="207-555-0117"; Services=0; Pass=$false; Eis=$null; Program=$null; Billing=$null }
    )

    foreach ($provider in $providers) {
        Invoke-SeedNonQuery @"
IF NOT EXISTS (SELECT 1 FROM dbo.Providers WHERE Name = @Name)
BEGIN
    INSERT dbo.Providers
        (Type, Name, Street, City, State, Zip, PrimaryContact, Phone, OfferedServices,
         ProvidesPassthroughService, BillingLocationEis, ProgramContact, BillingContact)
    VALUES
        (@Type, @Name, @Street, @City, @State, @Zip, @Contact, @Phone, @Services,
         @Pass, @Eis, @Program, @Billing);
END;
"@ $provider | Out-Null
    }

    Invoke-SeedNonQuery @"
UPDATE dbo.Providers
SET Street = COALESCE(Street, '225 Western Avenue'),
    City = COALESCE(City, 'Augusta'), State = COALESCE(State, 'ME'), Zip = COALESCE(Zip, '04330'),
    PrimaryContact = COALESCE(PrimaryContact, 'Gadget Coordinator'), Phone = COALESCE(Phone, '207-555-0100'),
    BillingLocationEis = COALESCE(BillingLocationEis, 'Maine AT Central Billing'),
    ProgramContact = COALESCE(ProgramContact, 'Pat Gear - program@maineat.demo'),
    BillingContact = COALESCE(BillingContact, 'Bill Ding - billing@maineat.demo')
WHERE Name = 'Maine AT Solutions';
"@ | Out-Null

    $peopleCommand = $connection.CreateCommand()
    $peopleCommand.Transaction = $transaction
    $peopleCommand.CommandText = @"
SELECT p.Id, p.UserId, p.FirstName, p.LastName, p.BirthDate, p.Gender, p.EffectiveDate,
       p.Bio, p.Waiver, p.DiagnosisCode, p.HasGuardian, p.GuardianName,
       p.PrimaryCareProvider, p.HealthcareSystemName, p.OpenWithVR,
       p.HasHomeSupport, p.HasSelfDirectedHomeSupport, p.HasSharedLiving,
       p.HasCommunitySupport1To1, p.HasCommunitySupportSelfDirected,
       p.HasCommunitySupportDayProgram, p.DayProgramCount,
       p.HasEmploymentSpecialist, p.HasWorkSupports, p.IsEmployed, p.EvergreenId,
       u.DisplayName AS CaseManagerName, u.Email AS CaseManagerEmail, u.Phone AS CaseManagerPhone
FROM dbo.People p
JOIN dbo.Users u ON u.Id = p.UserId
WHERE u.AgencyId = 2
ORDER BY p.Id;
"@
    $peopleReader = $peopleCommand.ExecuteReader()
    $peopleTable = [System.Data.DataTable]::new()
    $peopleTable.Load($peopleReader)
    $people = @($peopleTable.Rows)

    $providerCommand = $connection.CreateCommand()
    $providerCommand.Transaction = $transaction
    $providerCommand.CommandText = "SELECT Id, Name, PrimaryContact, Phone, ProvidesPassthroughService, BillingLocationEis, ProgramContact, BillingContact FROM dbo.Providers ORDER BY Name;"
    $providerReader = $providerCommand.ExecuteReader()
    $providerTable = [System.Data.DataTable]::new()
    $providerTable.Load($providerReader)
    $providerRows = @($providerTable.Rows)
    $waiverProviders = @($providerRows | Where-Object { $_.Name -in $providers.Name -and $_.Name -notlike '*Health*' -and $_.Name -notlike '*Technology*' })
    $passthroughProviders = @($providerRows | Where-Object { [bool]$_.ProvidesPassthroughService })

    $existingShowcaseNotes = [int](Invoke-SeedScalar "SELECT COUNT(*) FROM dbo.Notes WHERE CaseManagerJustification = @SeedTag" @{ SeedTag=$seedTag })
    if ($existingShowcaseNotes -eq 0) {
        $noteSql = @"
INSERT dbo.Notes
    (Narrative, Status, PersonId, EventDate, FormType, NoteType, AgencyId,
     ReturnReason, ReturnedById, ReturnedAt, ApprovedAt, ApprovedById,
     ComplianceOverride, Minutes, StartTime, CaseManagerJustification, VisitDocumentationJson)
VALUES
    (@Narrative, @Status, @PersonId, @EventDate, @FormType, @NoteType, 2,
     @ReturnReason, @ReturnedById, @ReturnedAt, @ApprovedAt, @ApprovedById,
     0, @Minutes, @StartTime, @SeedTag, @VisitJson);
"@
        foreach ($person in $people) {
            $theme = Get-ShowTheme ([int]$person.Id)
            $name = "$($person.FirstName) $($person.LastName)".Trim()
            $offset = (([int]$person.Id * 11) % 170) + 2
            $visitDate = $today.AddDays(-$offset)
            $visitJson = [ordered]@{
                setting = 2; appearance = 1; participation = if (([int]$person.Id % 4) -eq 0) { 2 } else { 1 }
                safetyObservation = 1; consumerPresent = $true; expressedPreferences = $true
                askedQuestions = (([int]$person.Id % 3) -ne 0); madeChoices = $true
                communicationSupportUsed = (([int]$person.Id % 5) -eq 0); goalsReviewed = $true
                servicesDiscussed = $true; documentsReviewed = $false
                settingDetails = $theme.Location
                observationDetails = "No immediate health or safety concern was observed. $($theme.Distraction)."
                additionalAttendees = "The supporting cast remained in a supporting role."
                attendees = @()
            } | ConvertTo-Json -Depth 8 -Compress

            $visitNarrative = "$name participated in a scheduled face-to-face visit at $($theme.Location). We reviewed current choices, transportation, and the goal of $($theme.Skill). $($theme.Distraction). $($person.FirstName) selected the next activity, explained the reason for that choice, and identified one backup option. No immediate health or safety concern was observed. Plan: the case manager will confirm transportation and follow up after the activity."
            Invoke-SeedNonQuery $noteSql @{
                Narrative=$visitNarrative; Status=6; PersonId=[int]$person.Id; EventDate=$visitDate
                FormType=$null; NoteType=0; ReturnReason=$null; ReturnedById=$null; ReturnedAt=$null
                ApprovedAt=$visitDate.AddDays(1); ApprovedById=1007; Minutes=60
                StartTime=(([int]$person.Id * 13) % 420); SeedTag=$seedTag; VisitJson=$visitJson
            } | Out-Null

            $contactDate = $visitDate.AddDays(-9)
            $contactNarrative = "The case manager contacted $name to review the next step for $($theme.Skill). $($person.FirstName) restated the plan in their own words and requested one reminder on the morning of the activity. $($theme.Distraction). The case manager confirmed that declining or rescheduling remains an available choice. Plan: send the agreed reminder and document whether the arrangement worked."
            Invoke-SeedNonQuery $noteSql @{
                Narrative=$contactNarrative; Status=6; PersonId=[int]$person.Id; EventDate=$contactDate
                FormType=$null; NoteType=1; ReturnReason=$null; ReturnedById=$null; ReturnedAt=$null
                ApprovedAt=$contactDate.AddDays(1); ApprovedById=1007; Minutes=30
                StartTime=(([int]$person.Id * 17) % 420); SeedTag=$seedTag; VisitJson=$null
            } | Out-Null

            $progressDate = $visitDate.AddDays(-21)
            $returned = (([int]$person.Id % 11) -eq 0)
            $progressNarrative = "$name practiced $($theme.Skill) using a short written checklist. $($person.FirstName) completed the first steps independently, asked for clarification once, and chose to repeat the final step. The checklist worked better than verbal reminders. Plan: use the same checklist twice before the next review and let $($person.FirstName) decide whether any step should be rewritten."
            Invoke-SeedNonQuery $noteSql @{
                Narrative=$progressNarrative; Status=if ($returned) { 7 } else { 2 }; PersonId=[int]$person.Id; EventDate=$progressDate
                FormType=$null; NoteType=3
                ReturnReason=if ($returned) { "Please clarify who provided the checklist; 'a mysterious benefactor' is not billable documentation." } else { $null }
                ReturnedById=if ($returned) { 1007 } else { $null }; ReturnedAt=if ($returned) { $progressDate.AddDays(2) } else { $null }
                ApprovedAt=$null; ApprovedById=$null; Minutes=45
                StartTime=(([int]$person.Id * 19) % 420); SeedTag=$seedTag; VisitJson=$null
            } | Out-Null
        }
    }

    # Associate active support-network contacts with directory organizations by
    # exact Organization name; these are synthetic profile contacts, not history.
    for ($index = 0; $index -lt $people.Count; $index++) {
        if (($index % 2) -ne 0) { continue }
        $person = $people[$index]
        $provider = $waiverProviders[$index % $waiverProviders.Count]
        $email = "support$($person.Id)@demo.sati.invalid"
        Invoke-SeedNonQuery @"
IF NOT EXISTS (SELECT 1 FROM dbo.PersonContacts WHERE PersonId=@PersonId AND Email=@Email)
INSERT dbo.PersonContacts
    (PersonId, FirstName, LastName, Kind, Relationship, Organization, Phone, Email,
     IsEmergencyContact, HasActiveRelease, IsActive)
VALUES
    (@PersonId, @FirstName, @LastName, 'ServiceProvider', 'Community support coordinator',
     @Organization, @Phone, @Email, 0, 1, 1);
"@ @{
            PersonId=[int]$person.Id; FirstName="Casey"; LastName="Coordinator"
            Organization=[string]$provider.Name; Phone=[string]$provider.Phone; Email=$email
        } | Out-Null
    }

    # Complete comprehensive-assessment form rows for every demo consumer. PCP
    # completion remains a cross-section so the dashboard retains varied states.
    for ($index = 0; $index -lt $people.Count; $index++) {
        $person = $people[$index]
        if ($person.EffectiveDate -is [DBNull]) { continue }
        $effective = [DateTime]$person.EffectiveDate
        $cycleStart = $effective
        while ($cycleStart.AddYears(1) -le $today) { $cycleStart = $cycleStart.AddYears(1) }
        while ($cycleStart -gt $today) { $cycleStart = $cycleStart.AddYears(-1) }
        $cycleEnd = $cycleStart.AddYears(1)
        $completedOn = $today.AddDays(-((([int]$person.Id * 5) % 90) + 3))

        foreach ($formSpec in @(
            @{ Type="PCP"; Enum=4; Label="Person-Centered Plan"; Joke="The plan contains goals, responsible parties, and zero secret identities." },
            @{ Type="ComprehensiveAssessment"; Enum=5; Label="Comprehensive Assessment"; Joke="Every domain was addressed; no answer was simply 'everybody lies.'" }
        )) {
            if ($formSpec.Type -eq "PCP" -and ($index % 2) -ne 0) { continue }
            $formId = Invoke-SeedScalar @"
SELECT TOP (1) Id FROM dbo.Forms
WHERE PersonId=@PersonId AND Type=@Type AND DueDate>@CycleStart AND DueDate<=@CycleEnd
ORDER BY DueDate DESC;
"@ @{ PersonId=[int]$person.Id; Type=$formSpec.Type; CycleStart=$cycleStart; CycleEnd=$cycleEnd }
            if ($null -eq $formId -or $formId -is [DBNull]) { continue }
            Invoke-SeedNonQuery "UPDATE dbo.Forms SET IsCompliant=1, CompletedDate=@CompletedOn, OpenedDate=COALESCE(OpenedDate, DATEADD(day,-7,@CompletedOn)) WHERE Id=@Id;" @{ CompletedOn=$completedOn; Id=[int]$formId } | Out-Null

            $formMarker = "$seedTag`_FORM_$($formSpec.Type)_$($person.Id)"
            $formNarrative = "$($formSpec.Label) for $($person.FirstName) $($person.LastName) was completed with the person's preferences, strengths, chosen outcomes, provider responsibilities, and follow-up dates documented. $($formSpec.Joke) Copies and next steps were reviewed in plain language."
            $matchingFormNotes = [int](Invoke-SeedScalar "SELECT COUNT(*) FROM dbo.Notes WHERE CaseManagerJustification=@Marker" @{ Marker=$formMarker })
            if ($matchingFormNotes -eq 0) {
                Invoke-SeedNonQuery @"
INSERT dbo.Notes
    (Narrative, Status, PersonId, EventDate, FormType, NoteType, AgencyId,
     ApprovedAt, ApprovedById, ComplianceOverride, Minutes, StartTime, CaseManagerJustification)
VALUES
    (@Narrative, 6, @PersonId, @EventDate, @FormType, 2, 2,
     DATEADD(day,1,@EventDate), 1007, 0, 60, @StartTime, @Marker);
"@ @{
                    Narrative=$formNarrative; PersonId=[int]$person.Id; EventDate=$completedOn
                    FormType=[int]$formSpec.Enum; StartTime=(([int]$person.Id * 23) % 420); Marker=$formMarker
                } | Out-Null
            }
        }
    }

    # Approved assessments power both the assessment viewer and the PCP source.
    # Approved versions are immutable, so earlier seed versions remain intact;
    # the newest populated version becomes authoritative through normal ordering.
    # Empty editable shells are safe to hydrate and otherwise hide that content
    # because the assessment workspace intentionally prioritizes author drafts.
    for ($index = 0; $index -lt $people.Count; $index++) {
        $person = $people[$index]
        $provider = $waiverProviders[$index % $waiverProviders.Count]
        $theme = Get-ShowTheme ([int]$person.Id)
        $associateProvider = (($index % 2) -eq 0)
        $document = New-AssessmentDocument $person $theme ([int]$provider.Id) ([string]$provider.Name) $associateProvider
        $documentJson = $document | ConvertTo-Json -Depth 20 -Compress

        $emptyEditableAssessmentId = Invoke-SeedScalar @"
SELECT TOP (1) Id
FROM dbo.ComprehensiveAssessments
WHERE PersonId=@PersonId
  AND AuthorUserId=@AuthorUserId
  AND Status IN ('Draft','Returned')
  AND ISJSON(DocumentJson)=1
  AND COALESCE(JSON_QUERY(DocumentJson,'$.contributors'),'[]')='[]'
  AND COALESCE(JSON_QUERY(DocumentJson,'$.needs'),'[]')='[]'
  AND NOT EXISTS (
      SELECT 1
      FROM OPENJSON(DocumentJson,'$.answers')
      WITH (
          status int '$.status',
          narrative nvarchar(max) '$.narrative',
          supports int '$.supports',
          supportDetails nvarchar(max) '$.supportDetails',
          exceptionReason nvarchar(max) '$.exceptionReason',
          dissentingOpinion nvarchar(max) '$.dissentingOpinion',
          yesNoResponse bit '$.yesNoResponse',
          followUpYesNoResponse bit '$.followUpYesNoResponse',
          details nvarchar(max) '$.details',
          therapySessionFormat int '$.therapySessionFormat',
          wantsOtherSessionFormat bit '$.wantsOtherSessionFormat',
          wantsFrequencyChange bit '$.wantsFrequencyChange',
          therapyFrequencyDirection int '$.therapyFrequencyDirection',
          activitySupportLevels nvarchar(max) '$.activitySupportLevels' AS JSON,
          activitySkillsTraining nvarchar(max) '$.activitySkillsTraining' AS JSON
      ) answer
      WHERE answer.status<>5
         OR NULLIF(LTRIM(RTRIM(COALESCE(answer.narrative,''))),'') IS NOT NULL
         OR COALESCE(answer.supports,0)<>0
         OR NULLIF(LTRIM(RTRIM(COALESCE(answer.supportDetails,''))),'') IS NOT NULL
         OR NULLIF(LTRIM(RTRIM(COALESCE(answer.exceptionReason,''))),'') IS NOT NULL
         OR NULLIF(LTRIM(RTRIM(COALESCE(answer.dissentingOpinion,''))),'') IS NOT NULL
         OR answer.yesNoResponse IS NOT NULL
         OR answer.followUpYesNoResponse IS NOT NULL
         OR NULLIF(LTRIM(RTRIM(COALESCE(answer.details,''))),'') IS NOT NULL
         OR COALESCE(answer.therapySessionFormat,0)<>0
         OR answer.wantsOtherSessionFormat IS NOT NULL
         OR answer.wantsFrequencyChange IS NOT NULL
         OR COALESCE(answer.therapyFrequencyDirection,0)<>0
         OR EXISTS (SELECT 1 FROM OPENJSON(answer.activitySupportLevels) WHERE TRY_CONVERT(int,[value])<>0)
         OR EXISTS (SELECT 1 FROM OPENJSON(answer.activitySkillsTraining) WHERE [value]='true')
  )
ORDER BY Version DESC;
"@ @{ PersonId=[int]$person.Id; AuthorUserId=[int]$person.UserId }
        if ($null -ne $emptyEditableAssessmentId -and $emptyEditableAssessmentId -isnot [DBNull]) {
            Invoke-SeedNonQuery @"
UPDATE dbo.ComprehensiveAssessments
SET DocumentJson=@DocumentJson, UpdatedAt=@UpdatedAt
WHERE Id=@AssessmentId AND Status IN ('Draft','Returned');
"@ @{
                AssessmentId=[int]$emptyEditableAssessmentId
                DocumentJson=$documentJson
                UpdatedAt=$today
            } | Out-Null
        }

        $hasCurrentProfile = [int](Invoke-SeedScalar @"
SELECT COUNT(*)
FROM dbo.ComprehensiveAssessments
WHERE PersonId=@PersonId
  AND Status='Approved'
  AND DocumentJson LIKE '%Confirm current waiver-service authorizations%';
"@ @{ PersonId=[int]$person.Id })
        if ($hasCurrentProfile -gt 0) { continue }

        $version = 1 + [int](Invoke-SeedScalar "SELECT COALESCE(MAX(Version),0) FROM dbo.ComprehensiveAssessments WHERE PersonId=@PersonId;" @{ PersonId=[int]$person.Id })
        $approvedOn = $today.AddDays(-((([int]$person.Id * 3) % 100) + 5))
        Invoke-SeedNonQuery @"
INSERT dbo.ComprehensiveAssessments
    (PersonId, AuthorUserId, Status, Version, CreatedAt, UpdatedAt,
     SubmittedAt, ApprovedAt, ApprovedByUserId, DocumentJson)
VALUES
    (@PersonId, @AuthorUserId, 'Approved', @Version, DATEADD(day,-14,@ApprovedOn), @ApprovedOn,
     DATEADD(day,-3,@ApprovedOn), @ApprovedOn, 1007, @DocumentJson);
"@ @{
            PersonId=[int]$person.Id; AuthorUserId=[int]$person.UserId; Version=$version
            ApprovedOn=$approvedOn; DocumentJson=$documentJson
        } | Out-Null
    }

    # A selection of playful but structurally valid AT requests and line items.
    for ($index = 0; $index -lt [Math]::Min(36, $people.Count); $index++) {
        $person = $people[$index * 4]
        $provider = $passthroughProviders[$index % $passthroughProviders.Count]
        $requestMarker = "showcase-at-$($person.Id)@demo.sati.invalid"
        if ([int](Invoke-SeedScalar "SELECT COUNT(*) FROM dbo.ATRequests WHERE CaseManagerEmail=@Marker;" @{ Marker=$requestMarker }) -gt 0) { continue }
        $status = @("Approved", "Review", "Received", "Development")[$index % 4]
        $submitted = $today.AddDays(-(($index * 4) + 10))
        $decision = if ($status -in @("Approved", "Received")) { $submitted.AddDays(6) } else { $null }
        $requestId = [int](Invoke-SeedScalar @"
INSERT dbo.ATRequests
    (PersonId, ClientName, ClientEvergreenId, CaseManagerName, CaseManagerEmail,
     CaseManagerPhone, CaseManagerAgency, VendorName, VendorBillingLocation,
     VendorProgramContact, VendorBillingContact, SalesTax, SubmittedDate, DecisionDate, Status, SnapshotPng)
OUTPUT INSERTED.Id
VALUES
    (@PersonId, @ClientName, @EvergreenId, @CaseManagerName, @Marker,
     @CaseManagerPhone, 'Sandbox Mode', @VendorName, @BillingLocation,
     @ProgramContact, @BillingContact, @SalesTax, @SubmittedDate, @DecisionDate, @Status, NULL);
"@ @{
            PersonId=[int]$person.Id; ClientName="$($person.FirstName) $($person.LastName)".Trim()
            EvergreenId=if ($person.EvergreenId -is [DBNull]) { $null } else { [string]$person.EvergreenId }
            CaseManagerName=[string]$person.CaseManagerName; Marker=$requestMarker
            CaseManagerPhone=if ($person.CaseManagerPhone -is [DBNull]) { "207-555-0199" } else { [string]$person.CaseManagerPhone }
            VendorName=[string]$provider.Name
            BillingLocation=if ($provider.BillingLocationEis -is [DBNull]) { $null } else { [string]$provider.BillingLocationEis }
            ProgramContact=if ($provider.ProgramContact -is [DBNull]) { $null } else { [string]$provider.ProgramContact }
            BillingContact=if ($provider.BillingContact -is [DBNull]) { $null } else { [string]$provider.BillingContact }
            SalesTax=[decimal](8.25 + ($index % 5)); SubmittedDate=$submitted; DecisionDate=$decision; Status=$status
        })
        foreach ($item in @(
            @{ Name="Noise-cancelling headphones for dramatic hallway scenes"; Cost=[decimal](89.95 + $index); Qty=1; Url="https://demo.invalid/quiet-hero-headphones" },
            @{ Name="Visual schedule tablet stand (cape-safe edition)"; Cost=[decimal](42.50 + ($index % 7)); Qty=1 + ($index % 2); Url="https://demo.invalid/visual-schedule-stand" }
        )) {
            Invoke-SeedNonQuery "INSERT dbo.ATRequestItems (ATRequestId,Name,ItemCost,Quantity,Url) VALUES (@RequestId,@Name,@Cost,@Qty,@Url);" @{ RequestId=$requestId; Name=$item.Name; Cost=$item.Cost; Qty=$item.Qty; Url=$item.Url } | Out-Null
        }
    }

    $transaction.Commit()
}
catch {
    try { $transaction.Rollback() } catch { }
    throw
}
finally {
    $connection.Dispose()
}

# Read-back validation occurs on a new connection after commit.
$check = [System.Data.SqlClient.SqlConnection]::new($connectionString)
$check.Open()
$checkCommand = $check.CreateCommand()
$checkCommand.CommandText = @"
SELECT
    DB_NAME() AS DatabaseName,
    (SELECT EnvironmentName FROM dbo.SatiDatabaseIdentity WHERE Id=1) AS EnvironmentName,
    (SELECT COUNT(*) FROM dbo.Providers) AS Providers,
    (SELECT COUNT(*) FROM dbo.Notes WHERE CaseManagerJustification LIKE 'DEMO_SHOWCASE_V1%') AS ShowcaseNotes,
    (SELECT COUNT(*) FROM dbo.Forms WHERE IsCompliant=1 AND CompletedDate IS NOT NULL) AS CompletedForms,
    (SELECT COUNT(*) FROM dbo.ComprehensiveAssessments WHERE Status='Approved') AS ApprovedAssessments,
    (SELECT COUNT(*) FROM dbo.PersonContacts WHERE Email LIKE '%@demo.sati.invalid') AS ShowcaseContacts,
    (SELECT COUNT(*) FROM dbo.ATRequests WHERE CaseManagerEmail LIKE 'showcase-at-%@demo.sati.invalid') AS ShowcaseATRequests;
"@
$reader = $checkCommand.ExecuteReader()
$result = [System.Data.DataTable]::new()
$result.Load($reader)
$check.Dispose()
$result | Format-Table -AutoSize
