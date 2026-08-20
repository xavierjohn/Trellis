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

// Both violation arrays land in ProblemDetails.Extensions, which is object-valued, so STJ
// resolves them polymorphically at write time and native AOT needs each array type rooted
// here explicitly. Registering the element type alone is not enough. Any AOT consumer of
// Trellis.Asp needs these same two entries; see trellis-api-asp.md.
[JsonSerializable(typeof(FieldViolationProblemDetail[]))]
[JsonSerializable(typeof(RuleViolationProblemDetail[]))]
internal sealed partial class ShowcaseJsonSerializerContext : JsonSerializerContext;