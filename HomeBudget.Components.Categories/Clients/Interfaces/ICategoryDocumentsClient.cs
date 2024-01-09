﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Accounting.Infrastructure.Clients.Interfaces;
using HomeBudget.Components.Categories.Models;

namespace HomeBudget.Components.Categories.Clients.Interfaces
{
    public interface ICategoryDocumentsClient : IDocumentClient<CategoryDocument>
    {
        Task<Result<IReadOnlyCollection<CategoryDocument>>> GetAsync();

        Task<Result<CategoryDocument>> GetByIdAsync(Guid contractorId);

        Task<Result<string>> InsertOneAsync(Category payload);

        Task<bool> CheckIfExistsAsync(string contractorKey);
    }
}