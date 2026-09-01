namespace DrCare.Application.Leads;

public sealed record LocationAnalysisQuestionDefinition(
    string Code,
    string Group,
    string Prompt,
    string? Hint = null);

public static class LocationAnalysisCatalog
{
    public static readonly string[] ResponseCodes = ["YES_COMPLETELY", "YES_PARTIALLY", "NO", "DONT_KNOW"];

    public static readonly IReadOnlyDictionary<string, string> ResponseLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["YES_COMPLETELY"] = "Yes, completely",
        ["YES_PARTIALLY"] = "Yes, partially",
        ["NO"] = "No",
        ["DONT_KNOW"] = "Don't know",
    };

    public static readonly string[] Groups =
    [
        "Site & Lease",
        "Physical Requirements",
        "Traffic & Accessibility",
        "Market & Competition",
        "Community & Environment",
        "Growth & Sales Potential",
    ];

    public static readonly string[] LeaseOwnershipOptions = ["LEASED", "OWNED", "SHARED", "PENDING", "DONT_KNOW"];

    public static readonly IReadOnlyList<LocationAnalysisQuestionDefinition> Questions =
    [
        Q("SITE_01", "Site & Lease", "Does the location meet the location/site/guidelines/suggestions?"),
        Q("SITE_02", "Site & Lease", "Does the location match the fitness of purpose and the franchise concept?"),
        Q("SITE_03", "Site & Lease", "Is the monthly rental fees reasonable for the intended location?"),
        Q("SITE_04", "Site & Lease", "Is the terms of the lease generally favorable to the franchisee?"),
        Q("SITE_05", "Site & Lease", "Is the area classified as city, 1st or 2nd class municipality?"),
        Q("PHYSICAL_01", "Physical Requirements", "Is the lease property located in the ground floor of the building with other business leasing the property?"),
        Q("PHYSICAL_02", "Physical Requirements", "Is your location in an area without zonal restriction in operating a pharmacy?"),
        Q("PHYSICAL_03", "Physical Requirements", "Does the size fit the franchisor’s standard requirements?", "3-meter front area; 30 sqm floor area."),
        Q("PHYSICAL_04", "Physical Requirements", "Do they have the electrical, plumbing and telecommunication capability needed to run the business equipment from your Franchise?"),
        Q("PHYSICAL_05", "Physical Requirements", "Is the location visibly and readily exposed? Is the location house in a well-constructed relatively new building?"),
        Q("PHYSICAL_06", "Physical Requirements", "Is your area with readily available park port of at least 2 cars?"),
        Q("TRAFFIC_01", "Traffic & Accessibility", "Is the foot traffic likely to register at least 100 individuals per hour within the path leading to your location during the peak business hour?"),
        Q("TRAFFIC_02", "Traffic & Accessibility", "Is the foot traffic coming from residential locals and from other barangay/barrio areas?"),
        Q("TRAFFIC_03", "Traffic & Accessibility", "Are there establishment bring in foot traffic to the area? (E g. Churches, School, Hospital and Government Offices)"),
        Q("TRAFFIC_04", "Traffic & Accessibility", "Is the immediate street fronting your location be bi-directional 6-Lane main street?"),
        Q("TRAFFIC_05", "Traffic & Accessibility", "Is the street prompting your location causing traffic during peak business hours?"),
        Q("TRAFFIC_06", "Traffic & Accessibility", "Is the volume of vehicular traffic being at least a minimum of 100 vehicles of all sort within an hour period?"),
        Q("TRAFFIC_07", "Traffic & Accessibility", "Is the location accessible to at least 100 residential houses within 1 km radius?"),
        Q("TRAFFIC_08", "Traffic & Accessibility", "Is the location accessible in all directions to most commercial establishment?"),
        Q("MARKET_01", "Market & Competition", "Does the location and its vicinity have at least 10 reputable or established brand within a 1 km radius in reference to the location of interest?"),
        Q("MARKET_02", "Market & Competition", "Is there an animal bite center of another brand operating within 1 km radius in reference to your location of operation?"),
        Q("MARKET_03", "Market & Competition", "Is there the same franchisee within 1 km in that area? Did the franchisor follow the rules regarding awarding franchisee an area exclusively?"),
        Q("MARKET_04", "Market & Competition", "Are there at least 5 competitor animal bite centers within 1 km in the area?"),
        Q("MARKET_05", "Market & Competition", "Can you identify your biggest competitor in the area?"),
        Q("COMMUNITY_01", "Community & Environment", "Is your area considered within the vicinity of a tourist spot/tourism?"),
        Q("COMMUNITY_02", "Community & Environment", "Is there an annual event of citywide proportion that utilize the immediate street fronting your location?"),
        Q("COMMUNITY_03", "Community & Environment", "Are there current or future infrastructure developments in the area that may positively impact your franchise business? (Road Widening; Mid-Rise Condominium Bldg.)"),
        Q("GROWTH_01", "Growth & Sales Potential", "Do you likely to perceive a monthly sales projection between ₱150,000 - ₱300,000 for the potential location?"),
        Q("GROWTH_02", "Growth & Sales Potential", "Does your location create an atmosphere of “conveniently located “?"),
        Q("GROWTH_03", "Growth & Sales Potential", "Do you likely to perceive present and future growth potential within the area of operation?"),
        Q("GROWTH_04", "Growth & Sales Potential", "Is the area in which you are located is supported by a strong economic bas? (E g nearby industries working full time)"),
    ];

    public static bool IsValidQuestion(string code) => Questions.Any(x => x.Code.Equals(code, StringComparison.Ordinal));
    public static bool IsValidResponse(string code) => ResponseCodes.Contains(code, StringComparer.Ordinal);
    public static bool IsValidLeaseStatus(string code) => LeaseOwnershipOptions.Contains(code, StringComparer.Ordinal);
    public static string LabelForResponse(string code) => ResponseLabels.GetValueOrDefault(code, code);

    private static LocationAnalysisQuestionDefinition Q(string code, string group, string prompt, string? hint = null) => new(code, group, prompt, hint);
}
