using System.Text.Json;
using DrCare.Application.Contracts;
using DrCare.Domain;

namespace DrCare.Application.Settings;

public interface ISettingsService
{
    Task<PricingSettingsResponse> GetPricingAsync(CancellationToken ct);
    Task<PricingSettingsResponse> UpdatePricingAsync(UpdatePricingSettingsRequest request, CancellationToken ct);
    Task<InvoiceSettingsResponse> GetInvoiceAsync(CancellationToken ct);
    Task<InvoiceSettingsResponse> UpdateInvoiceAsync(UpdateInvoiceSettingsRequest request, CancellationToken ct);
    Task<IReadOnlyList<ContractTemplateResponse>> GetTemplatesAsync(CancellationToken ct);
    Task<ContractTemplateResponse> CreateTemplateAsync(CreateContractTemplateRequest request, CancellationToken ct);
    Task<IReadOnlyList<PreLaunchChecklistSettingsResponse>> GetChecklistsAsync(CancellationToken ct);
    Task<IReadOnlyList<PreLaunchChecklistSettingsResponse>> UpdateChecklistsAsync(UpdatePreLaunchChecklistSettingsRequest request, CancellationToken ct);
    Task<AnnualGoalSettingsResponse> GetAnnualGoalAsync(CancellationToken ct);
    Task<AnnualGoalSettingsResponse> UpdateAnnualGoalAsync(UpdateAnnualGoalSettingsRequest request, CancellationToken ct);
}

public sealed class SettingsService(ISettingsRepository settings, ICurrentUser currentUser) : ISettingsService
{
    public Task<PricingSettingsResponse> GetPricingAsync(CancellationToken ct) => GetAsync("pricing", new PricingSettingsResponse(320_000m, 350_000m, 600_000m, "PHP", DateTimeOffset.UtcNow), ct);
    public Task<InvoiceSettingsResponse> GetInvoiceAsync(CancellationToken ct) => GetAsync("invoice", new InvoiceSettingsResponse(50_000m, "PHP", 7, DateTimeOffset.UtcNow), ct);
    public Task<AnnualGoalSettingsResponse> GetAnnualGoalAsync(CancellationToken ct) => GetAsync("annual-goal", new AnnualGoalSettingsResponse(DateTime.UtcNow.Year, 50, DateTimeOffset.UtcNow), ct);
    public async Task<PricingSettingsResponse> UpdatePricingAsync(UpdatePricingSettingsRequest request, CancellationToken ct) => await PutAsync("pricing", new PricingSettingsResponse(request.AbcPrice, request.PharmacyPrice, request.ComboPrice, NormalizeCurrency(request.Currency), DateTimeOffset.UtcNow), ct);
    public async Task<InvoiceSettingsResponse> UpdateInvoiceAsync(UpdateInvoiceSettingsRequest request, CancellationToken ct) => await PutAsync("invoice", new InvoiceSettingsResponse(request.DefaultDownPayment, NormalizeCurrency(request.Currency), request.PaymentTermDays, DateTimeOffset.UtcNow), ct);
    public async Task<AnnualGoalSettingsResponse> UpdateAnnualGoalAsync(UpdateAnnualGoalSettingsRequest request, CancellationToken ct) => await PutAsync("annual-goal", new AnnualGoalSettingsResponse(DateTime.UtcNow.Year, request.Target, DateTimeOffset.UtcNow), ct);

    public async Task<IReadOnlyList<ContractTemplateResponse>> GetTemplatesAsync(CancellationToken ct)
    {
        var defaults = Array.Empty<ContractTemplateResponse>();
        var value = await GetAsync("contract-templates", defaults, ct); return value;
    }

    public async Task<ContractTemplateResponse> CreateTemplateAsync(CreateContractTemplateRequest request, CancellationToken ct)
    {
        var items = (await GetTemplatesAsync(ct)).ToList();
        if (items.Any(x => x.Code.Equals(request.Code.Trim(), StringComparison.OrdinalIgnoreCase) && x.Version.Equals(request.Version.Trim(), StringComparison.OrdinalIgnoreCase))) throw new ConflictException("A contract template with this code and version already exists.");
        var result = new ContractTemplateResponse(Guid.NewGuid(), request.Code.Trim(), request.Name.Trim(), request.Version.Trim(), true, DateTimeOffset.UtcNow);
        items.Add(result); await PutAsync("contract-templates", items, ct); return result;
    }

    public async Task<IReadOnlyList<PreLaunchChecklistSettingsResponse>> GetChecklistsAsync(CancellationToken ct)
    {
        var defaults = new[]
        {
            new PreLaunchChecklistSettingsResponse("ABC", AbcChecklist(), DateTimeOffset.UtcNow),
            new PreLaunchChecklistSettingsResponse("PHARMACY", PharmacyChecklist(), DateTimeOffset.UtcNow),
            new PreLaunchChecklistSettingsResponse("COMBO", AbcChecklist().Concat(PharmacyChecklist()).DistinctBy(x => x.Code).ToArray(), DateTimeOffset.UtcNow)
        };
        return await GetAsync("pre-launch-checklists", defaults, ct);
    }

    public async Task<IReadOnlyList<PreLaunchChecklistSettingsResponse>> UpdateChecklistsAsync(UpdatePreLaunchChecklistSettingsRequest request, CancellationToken ct)
    {
        if (request.Items.Count == 0 || request.Items.Count > 50) throw new ValidationException("Checklist must contain between 1 and 50 items.");
        if (request.Items.Any(x => string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Name))) throw new ValidationException("Checklist item code and name are required.");
        var value = new[] { new PreLaunchChecklistSettingsResponse("ALL", request.Items, DateTimeOffset.UtcNow) }; await PutAsync("pre-launch-checklists", value, ct); return value;
    }

    private async Task<T> GetAsync<T>(string key, T fallback, CancellationToken ct)
    {
        var setting = await settings.GetAsync(currentUser.OrganizationId, key, ct);
        if (setting is null) return fallback;
        return JsonSerializer.Deserialize<T>(setting.ValueJson) ?? fallback;
    }
    private async Task<T> PutAsync<T>(string key, T value, CancellationToken ct)
    {
        if (currentUser.Role != UserRole.MarketingAdmin) throw new ForbiddenException("Only the Marketing Admin can change workspace settings.");
        var json = JsonSerializer.Serialize(value);
        var setting = await settings.GetAsync(currentUser.OrganizationId, key, ct);
        if (setting is null) await settings.AddAsync(new AppSetting(currentUser.OrganizationId, key, json), ct); else setting.Update(json);
        await settings.SaveChangesAsync(ct); return value;
    }
    private static string NormalizeCurrency(string currency) { var value = currency.Trim().ToUpperInvariant(); if (value.Length != 3 || value.Any(c => c is < 'A' or > 'Z')) throw new ValidationException("Currency must be a three-letter code."); return value; }
    private static ChecklistTemplateItem[] AbcChecklist() =>
    [
        new("BUSINESS_PROPOSAL", "Business proposal completed", true),
        new("APPLICATION_FORM", "Franchise application form completed", true),
        new("FINANCING_PLAN", "Financing plan confirmed", true),
        new("BANK_DETAILS", "Bank details recorded", true),
        new("FULL_PAYMENT", "Full franchise payment completed", true),
        new("VALID_ID_TIN", "Valid ID and TIN received", true),
        new("BUSINESS_PLAN", "Franchisee business plan received", true),
        new("CLINICAL_STAFF", "Qualified doctor or nurse assigned", true),
        new("CLINICAL_CREDENTIALS", "Clinical staff credentials verified", true),
        new("ACKNOWLEDGEMENT_RECEIPT", "Acknowledgement receipt issued", true),
        new("OFFICIAL_RECEIPT", "Official receipt issued", true),
        new("FRANCHISE_AGREEMENT", "Signed franchise agreement filed", true),
        new("ABC_PRELAUNCH_CHECKLIST", "ABC pre-launch checklist completed", true),
        new("TRAINING_APPLICATION", "Training application completed", true),
        new("DOH_FLOOR_PLAN", "DOH floor plan and device layout prepared", true),
        new("PRELAUNCH_MEETING", "Pre-launch meeting completed", true),
        new("LOCATION_ANALYSIS", "Location analysis completed", true),
        new("LEASE_AND_ADDRESS", "Lease contract and exact site address received", true),
        new("DTI_REGISTRATION", "DTI registration received", true),
        new("SITE_PHOTOS", "Site photos received", true),
        new("PERMITS", "Required business permits completed", true),
        new("SIGNAGE_AND_COMPLIANCE", "Signage and print compliance completed", true),
        new("OPENING_STOCKS", "Opening stocks and requisition prepared", true),
        new("INVENTORY_PLAN", "Inventory and monthly purchasing plan confirmed", true),
        new("LEAD_TIME", "30-day launch lead time confirmed", true),
        new("GRAND_OPENING", "Grand opening schedule confirmed", true),
        new("ANIMAL_BITE_TRAINING", "Animal-bite training completed", true)
    ];

    private static ChecklistTemplateItem[] PharmacyChecklist() =>
    [
        new("PHARMACY_PRELAUNCH_CHECKLIST", "Standardized Pharmacy pre-launch checklist completed", true),
        new("PHARMACY_PERMITS", "Pharmacy licenses and permits completed", true),
        new("PHARMACY_STAFF", "Qualified pharmacy staff assigned", true),
        new("PHARMACY_TRAINING", "Pharmacy product and operations training completed", true),
        new("PHARMACY_SITE_READY", "Pharmacy site readiness verified", true),
        new("PHARMACY_OPENING_STOCKS", "Pharmacy opening stocks prepared", true)
    ];
}
