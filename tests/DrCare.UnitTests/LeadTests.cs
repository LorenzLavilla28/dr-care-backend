using DrCare.Domain;

namespace DrCare.UnitTests;

public sealed class LeadTests
{
    private static Lead CreateLead() => new(
        Guid.NewGuid(),
        "Alex Santos",
        "+639171234567",
        "alex@example.com",
        "Business income",
        Guid.NewGuid());

    [Fact]
    public void New_lead_can_start_inquiry()
    {
        var lead = CreateLead();

        lead.StartInquiry();

        Assert.Equal(LeadState.Inquiry, lead.State);
    }

    [Fact]
    public void Incomplete_inquiry_returns_missing_required_fields_and_creates_no_false_progress()
    {
        var lead = CreateLead();
        lead.StartInquiry();

        var accepted = lead.SubmitInquiry(out var missing);

        Assert.False(accepted);
        Assert.Equal(LeadState.InquiryIncomplete, lead.State);
        Assert.Contains("age", missing);
    }

    [Fact]
    public void Complete_inquiry_enters_nurturing()
    {
        var lead = CreateLead();
        lead.StartInquiry();
        lead.UpdateInquiry(null, 35, null, null, null, "Quezon City");

        var accepted = lead.SubmitInquiry(out var missing);

        Assert.True(accepted);
        Assert.Empty(missing);
        Assert.Equal(LeadState.Nurturing, lead.State);
    }

    [Fact]
    public void Qualification_is_rejected_before_nurturing()
    {
        var lead = CreateLead();

        var error = Assert.Throws<DomainRuleException>(() => lead.EvaluateQualification("qualified"));

        Assert.Contains("not valid", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(LeadState.New, lead.State);
    }

    [Fact]
    public void Product_pricing_defaults_actual_price_and_rejects_invalid_discount()
    {
        var lead = CreateLead();

        lead.SetProductLine(ProductLine.Abc, 320_000m);

        Assert.Equal(320_000m, lead.ListPrice);
        Assert.Equal(320_000m, lead.ActualPrice);
        Assert.Throws<DomainRuleException>(() => lead.SetProductLine(ProductLine.Abc, 320_000m, 330_000m));
    }

    [Fact]
    public void Down_payment_invoice_keeps_artifact_and_due_date()
    {
        var payment = new DownPayment(Guid.NewGuid(), Guid.NewGuid(), 50_000m, "PHP");
        var dueAt = DateTimeOffset.UtcNow.AddDays(7);

        payment.IssueInvoice("DP-001", "private/invoice.pdf", new string('a', 64), dueAt);

        Assert.Equal(DownPaymentStatus.Invoiced, payment.Status);
        Assert.Equal("private/invoice.pdf", payment.InvoiceObjectKey);
        Assert.Equal(dueAt, payment.InvoiceDueAt);
    }

    [Fact]
    public void Paused_prelaunch_item_cannot_be_completed()
    {
        var item = new PreLaunchItem(Guid.NewGuid(), "ANIMAL_BITE_TRAINING", "Animal-bite training", false, true);

        Assert.Throws<DomainRuleException>(() => item.Update(true, null));
    }

    [Fact]
    public void Prelaunch_requires_explicit_completion_after_required_items_are_checked()
    {
        var checklist = new PreLaunchChecklist(Guid.NewGuid(), Guid.NewGuid(), ProductLine.Abc);
        var item = new PreLaunchItem(checklist.Id, "SITE_READY", "Site readiness verified", true);
        checklist.AddItem(item);

        item.Update(true, "Verified");
        checklist.Touch();

        Assert.Equal("IN_PROGRESS", checklist.Status);

        checklist.Complete();

        Assert.Equal("COMPLETED", checklist.Status);
    }

    [Fact]
    public void Prelaunch_cannot_be_completed_with_missing_required_items()
    {
        var checklist = new PreLaunchChecklist(Guid.NewGuid(), Guid.NewGuid(), ProductLine.Abc);
        checklist.AddItem(new PreLaunchItem(checklist.Id, "SITE_READY", "Site readiness verified", true));

        Assert.Throws<DomainRuleException>(() => checklist.Complete());
        Assert.Equal("IN_PROGRESS", checklist.Status);
    }
}
