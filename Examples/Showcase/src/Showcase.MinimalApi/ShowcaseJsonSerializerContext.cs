namespace Trellis.Showcase.MinimalApi;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Trellis.Asp;
using Trellis.Showcase.Application.Features.SubmitBatchTransfers;
using Trellis.Showcase.Application.Models;
using Trellis.Showcase.MinimalApi.Endpoints;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(OpenAccountRequest))]
[JsonSerializable(typeof(DepositRequest))]
[JsonSerializable(typeof(WithdrawRequest))]
[JsonSerializable(typeof(SecureWithdrawRequest))]
[JsonSerializable(typeof(TransferRequest))]
[JsonSerializable(typeof(FreezeRequest))]
[JsonSerializable(typeof(InterestRequest))]
[JsonSerializable(typeof(BatchTransferEndpoints.BatchTransferRequest))]
[JsonSerializable(typeof(BatchMetadata))]
[JsonSerializable(typeof(BatchTransferLine))]
[JsonSerializable(typeof(AccountResponse))]
[JsonSerializable(typeof(PagedResponse<AccountResponse>))]
[JsonSerializable(typeof(PageLink))]
[JsonSerializable(typeof(BatchTransferReceipt))]
[JsonSerializable(typeof(ProblemDetails))]
[JsonSerializable(typeof(HttpValidationProblemDetails))]
[JsonSerializable(typeof(Dictionary<string, string[]>))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(RuleViolationProblemDetail[]))]
internal sealed partial class ShowcaseJsonSerializerContext : JsonSerializerContext;