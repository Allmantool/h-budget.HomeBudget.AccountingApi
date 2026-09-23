using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AutoMapper;
using Microsoft.AspNetCore.Mvc;

using HomeBudget.Accounting.Api.Constants;
using HomeBudget.Accounting.Api.Idempotency;
using HomeBudget.Accounting.Api.Models.Category;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Factories;
using HomeBudget.Components.Categories.Clients.Interfaces;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.Controllers
{
    [ApiController]
    [Route(Endpoints.Categories, Name = Endpoints.Categories)]
    public class CategoriesController(
        IMapper mapper,
        ICategoryDocumentsClient categoryDocumentsClient,
        ICategoryFactory categoryFactory) : ControllerBase
    {
        [HttpGet]
        public async Task<Result<IReadOnlyCollection<CategoryResponse>>> GetCategoriesAsync()
        {
            var documentsResult = await categoryDocumentsClient.GetAsync();

            if (!documentsResult.IsSucceeded)
            {
                return Result<IReadOnlyCollection<CategoryResponse>>.Failure();
            }

            var categories = documentsResult.Payload
                .Select(d => d.Payload)
                .OrderBy(c => c.CategoryKey)
                .ThenBy(op => op.OperationUnixTime)
                .ToList();

            return Result<IReadOnlyCollection<CategoryResponse>>.Succeeded(mapper.Map<IReadOnlyCollection<CategoryResponse>>(categories));
        }

        [HttpGet("byId/{categoryId}")]
        public async Task<Result<CategoryResponse>> GetCategoryByIdAsync(string categoryId)
        {
            if (!Guid.TryParse(categoryId, out var targetCategoryId))
            {
                return Result<CategoryResponse>.Failure($"Invalid '{nameof(targetCategoryId)}' has been provided");
            }

            var documentResult = await categoryDocumentsClient.GetByIdAsync(targetCategoryId);

            if (!documentResult.IsSucceeded || documentResult.Payload == null)
            {
                return Result<CategoryResponse>.Failure($"The category with '{targetCategoryId}' hasn't been found");
            }

            var document = documentResult.Payload;

            return Result<CategoryResponse>.Succeeded(mapper.Map<CategoryResponse>(document.Payload));
        }

        [HttpPost]
        public async Task<ActionResult<Result<Guid>>> CreateNewAsync(
            [FromBody] CreateCategoryRequest request,
            CancellationToken cancellationToken)
        {
            var newCategory = categoryFactory.Create(
                BaseEnumeration<CategoryTypes, int>.FromValue(request.CategoryType),
                request.NameNodes);

            var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                if (!ReferenceCreateContextFactory.TryCreate(
                    idempotencyKey,
                    ReferenceCreateFingerprint.Category(request),
                    request.SourceReference,
                    out var context))
                {
                    return BadRequest(Result<Guid>.Failure("A valid sourceReference and Idempotency-Key are required for an idempotent category create."));
                }

                var registration = await categoryDocumentsClient.InsertIdempotentAsync(
                    newCategory,
                    context,
                    cancellationToken);
                return registration.IsConflict
                    ? Conflict(Result<Guid>.Failure("The idempotency key has already been used for a different category request."))
                    : Result<Guid>.Succeeded(registration.TargetId);
            }

            var saveResult = await categoryDocumentsClient.InsertOneAsync(newCategory);

            return saveResult.IsSucceeded
                ? Result<Guid>.Succeeded(saveResult.Payload)
                : Result<Guid>.Failure(saveResult.StatusMessage);
        }
    }
}
