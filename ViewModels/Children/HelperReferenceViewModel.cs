using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace Sati.ViewModels.Children
{
    public record HelperItem(string Title, string Content);

    public record HelperGroup(string GroupTitle, List<HelperItem> Items);

    // The reference-article browser: a sidebar of grouped topics, click to read.
    // Extracted verbatim from the original HelpersViewModel when Helpers became a
    // sub-tab host. This is now the "Reference" sub-tab; behavior is unchanged
    // from the pre-refactor Helpers page.
    public partial class HelperReferenceViewModel : ObservableObject
    {
        [ObservableProperty] private HelperItem? selectedItem;
        [ObservableProperty] private string? selectedGroupTitle;

        public ObservableCollection<HelperGroup> Groups { get; } = [];

        [RelayCommand]
        private void SelectItem(HelperItem item)
        {
            SelectedItem = item;
            SelectedGroupTitle = Groups
                .FirstOrDefault(g => g.Items.Contains(item))?.GroupTitle;
        }

        public string ContentTitle => SelectedItem is null
            ? "Select a topic from the sidebar"
            : SelectedItem.Title;

        public string ContentBody => SelectedItem?.Content ?? string.Empty;
        public bool HasSelection => SelectedItem is not null;

        partial void OnSelectedItemChanged(HelperItem? value)
        {
            OnPropertyChanged(nameof(ContentTitle));
            OnPropertyChanged(nameof(ContentBody));
            OnPropertyChanged(nameof(HasSelection));
        }

        public HelperReferenceViewModel()
        {
            LoadGroups();
            SelectedItem = Groups.FirstOrDefault()?.Items.FirstOrDefault();
        }

        private void LoadGroups()
        {
            var groups = new[]
            {
                new HelperGroup("PCPs", new List<HelperItem>
                {
                    new("Overview",
                        "The Person-Centered Plan (PCP) is the foundational document that guides all services for a consumer on a Home and Community-Based Services waiver. It is developed collaboratively with the consumer, their support network, their support agency, and you as their case manager.\n\nThe PCP must be completed annually and signed by all parties before services can be authorized for the upcoming plan year. OADS requires that the plan reflect the consumer's own goals, preferences, and priorities — not simply a list of services.\n\nThe PCP process typically begins 60 to 90 days before the plan anniversary date. Use Sati's upcoming tasks panel to monitor when each consumer's PCP window is approaching. A late PCP can result in a gap in service authorization, which directly affects the consumer's care."),

                    new("Goals",
                        "The Goals section of the PCP documents what the consumer wants to achieve over the coming plan year. Goals should be written in the consumer's own voice wherever possible, and should reflect their personal aspirations — not just service-related outcomes.\n\nEach goal should include a description of what the consumer wants to accomplish, the supports that will help them get there, and how progress will be measured. OADS expects goals to be meaningful and individualized — avoid generic language like 'maintain current level of functioning.'\n\nGood examples of consumer-driven goals include: 'I want to learn to cook two meals independently by June,' or 'I want to attend a community event at least once a month.' Work with the consumer and their support team to identify goals that are realistic, motivating, and connected to their broader vision for their life."),

                    new("Services",
                        "The Services section of the PCP documents all waiver-funded services the consumer will receive during the plan year, including the type of service, the frequency and duration, the provider agency, and the authorized funding amount.\n\nServices must be directly tied to the consumer's assessed needs and goals. OADS reviewers will look for a clear connection between the consumer's SIP scores, their goals, and the services being requested. Services that cannot be justified by the consumer's functional needs or goals are at risk of being denied or reduced during plan review.\n\nCommon waiver services include Home Support, Community Support, Employment Support, Shared Living, Assistive Technology, and waiver-funded goods and services. If a consumer's needs have changed significantly since the last plan year, update their SIP before submitting the PCP to ensure the assessment reflects current needs."),

                    new("Signatures",
                        "The PCP requires signatures from the consumer, their legal representative if applicable, the support agency, and the case manager. OADS requires original signatures — electronic signatures are not currently accepted for the PCP unless your agency has a specific agreement in place with OADS.\n\nCollect signatures at the PCP meeting wherever possible. If the consumer or a team member is unable to attend in person, arrange for the document to be mailed or delivered for signature within five business days of the meeting.\n\nDocument the signature collection process in a service note, including the date the plan was presented, who signed, and how any missing signatures were obtained. If a team member refuses to sign, document the refusal and contact your OADS liaison for guidance. A PCP without all required signatures cannot be submitted for authorization."),
                }),

                new HelperGroup("Funding Requests", new List<HelperItem>
                {
                    new("Assistive Technology",
                        "Assistive Technology (AT) funding requests require a completed AT assessment by a qualified AT specialist, a written recommendation specifying the device or equipment, and a prior authorization request submitted to MaineCare.\n\nBegin by scheduling an AT assessment through your agency's AT coordinator or an independent specialist. The assessment report must document the consumer's functional limitations, how the requested technology addresses those limitations, and why less expensive alternatives are not appropriate.\n\nSubmit the prior authorization request with the assessment report, a vendor quote, and the consumer's most recent SIP. MaineCare typically takes 10 to 15 business days to process AT requests. If the request is denied, you have 30 days to file an appeal. Contact your OADS liaison before appealing to determine whether additional documentation might resolve the denial without a formal appeal."),

                    new("Goods and Services",
                        "Waiver-funded goods and services cover items that are directly related to the consumer's disability-related needs and that are not covered by other funding sources. Common examples include adaptive equipment for the home, specialized clothing, communication devices, and sensory supports.\n\nTo request goods and services funding, document the item in the consumer's PCP with a clear justification linking the item to the consumer's assessed needs. For items over a certain dollar threshold — typically $500 — a prior authorization request is required. Check your agency's current threshold with your supervisor.\n\nGoods and services funding cannot be used for items that are considered standard household expenses, personal care items available through MaineCare, or items that a support agency is responsible for providing as part of their service contract. If you are unsure whether an item qualifies, contact your OADS liaison before making any commitments to the consumer."),

                    new("LIHEAP",
                        "The Low Income Home Energy Assistance Program (LIHEAP) provides federally funded assistance with heating and utility costs for income-eligible households. Many of your consumers may qualify.\n\nApplications are typically accepted from November through April, though emergency assistance may be available year-round. Consumers can apply through their local Community Action Program (CAP) agency. In Maine, CAP agencies include Aroostook County Action Program, Western Maine Community Action, and others depending on the consumer's county of residence.\n\nTo assist a consumer with a LIHEAP application, gather documentation of household income, a recent utility bill, and proof of residency. If the consumer has a representative payee or guardian, they will need to be involved in the application process. LIHEAP assistance does not affect MaineCare eligibility and should be pursued whenever a consumer is struggling with energy costs."),

                    new("Housing Support",
                        "Waiver-funded housing supports can include home modifications, accessibility equipment, and in some cases, assistance with housing-related expenses that are directly tied to the consumer's disability-related needs.\n\nHome modifications — such as ramp installation, grab bars, or widened doorways — require a prior authorization request with a written assessment from an occupational therapist or other qualified professional documenting the need. Get at least two contractor quotes before submitting the request.\n\nFor consumers who are homeless or at risk of losing housing, connect them with your local Continuum of Care (CoC) and Maine's Housing First programs. Waiver funding cannot be used for rent or mortgage payments, but it can fund supports that help a consumer maintain stable housing. Document all housing-related advocacy in service notes, as this is an area of increasing scrutiny in OADS quality reviews."),

                    new("Durable Medical Equipment",
                        "Durable Medical Equipment (DME) is covered through MaineCare rather than waiver funding in most cases. This includes wheelchairs, hospital beds, walkers, and similar equipment. The consumer's primary care physician must write a prescription documenting the medical necessity of the equipment.\n\nTo initiate a DME request, work with the consumer's physician to obtain a prescription and a letter of medical necessity. The consumer's MaineCare plan will determine which DME providers are in network — check the MaineCare provider portal before ordering from a specific vendor.\n\nIf MaineCare denies a DME request, explore whether the item can be funded through waiver goods and services as an alternative. Document all steps taken to obtain DME in service notes, including the prescription date, the provider contacted, and the outcome of any prior authorization requests. Delays in DME can significantly affect a consumer's safety and quality of life — escalate to your supervisor if a request has been pending for more than 30 days."),
                }),
            };

            foreach (var group in groups)
                Groups.Add(group);
        }
    }
}