using DrCare.Application.Contracts;
using DrCare.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DrCare.Api.Controllers;

[ApiController, Route("api/v1/reports"), Authorize, EnableRateLimiting("api")]
public sealed class ReportsController(IReportService reports) : ControllerBase
{
    [HttpGet("overview")] public async Task<ActionResult<OverviewReport>> Overview([FromQuery] ReportQuery query, CancellationToken ct) => Ok(await reports.OverviewAsync(query, ct));
    [HttpGet("pipeline")] public async Task<ActionResult<PipelineReport>> Pipeline([FromQuery] ReportQuery query, CancellationToken ct) => Ok(await reports.PipelineAsync(query, ct));
    [HttpGet("conversion")] public async Task<ActionResult<ConversionReport>> Conversion([FromQuery] ReportQuery query, CancellationToken ct) => Ok(await reports.ConversionAsync(query, ct));
    [HttpGet("goals")] public async Task<ActionResult<GoalReport>> Goals([FromQuery] ReportQuery query, CancellationToken ct) => Ok(await reports.GoalsAsync(query, ct));
    [HttpGet("agent-leaderboard")] public async Task<ActionResult<IReadOnlyList<LeaderboardItem>>> Leaderboard([FromQuery] ReportQuery query, CancellationToken ct) => Ok(await reports.LeaderboardAsync(query, ct));
    [HttpGet("down-payments")] public async Task<ActionResult<DownPaymentReport>> DownPayments([FromQuery] ReportQuery query, CancellationToken ct) => Ok(await reports.DownPaymentsAsync(query, ct));
}

[ApiController, Route("api/v1"), Authorize, EnableRateLimiting("api")]
public sealed class OperationalQueuesController(IReportService reports) : ControllerBase
{
    [HttpGet("finance/workbench"), Authorize(Roles = "Finance,Leadership")] public async Task<ActionResult<FinanceWorkbenchResponse>> FinanceWorkbench([FromQuery] FinanceWorkbenchQuery query, CancellationToken ct) => Ok(await reports.FinanceWorkbenchAsync(query, ct));
    [HttpGet("finance/payments/{paymentId:guid}"), Authorize(Roles = "Finance,Leadership")] public async Task<ActionResult<FinancePaymentDetail>> FinancePaymentDetail(Guid paymentId, CancellationToken ct) => Ok(await reports.FinancePaymentDetailAsync(paymentId, ct));
    [HttpGet("finance/payment-queue"), Authorize(Roles = "Finance,Leadership")] public async Task<ActionResult<IReadOnlyList<QueueItem>>> Finance(CancellationToken ct) => Ok(await reports.FinanceQueueAsync(ct));
    [HttpGet("gm/contract-review-queue")] public async Task<ActionResult<IReadOnlyList<QueueItem>>> GeneralManager(CancellationToken ct) => Ok(await reports.GeneralManagerQueueAsync(ct));
    [HttpGet("admin/endorsement-queue")] public async Task<ActionResult<IReadOnlyList<QueueItem>>> Admin(CancellationToken ct) => Ok(await reports.AdminQueueAsync(ct));
}

[ApiController, Route("api/v1/audit-logs"), Authorize, EnableRateLimiting("api")]
public sealed class AuditLogsController(IReportService reports) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<PagedResponse<AuditLogResponse>>> List([FromQuery] Guid? leadId, [FromQuery] int page = 1, CancellationToken ct = default) => Ok(await reports.AuditLogsAsync(leadId, page, ct));
}
