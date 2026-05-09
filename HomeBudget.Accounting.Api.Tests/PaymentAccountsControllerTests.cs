using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;

using FluentAssertions;

using HomeBudget.Accounting.Api.Controllers;
using HomeBudget.Accounting.Api.Models.PaymentAccount;
using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.Models;
using HomeBudget.Core.Models;

namespace HomeBudget.Accounting.Api.Tests
{
    [TestFixture]
    public class PaymentAccountsControllerTests
    {
        [Test]
        public async Task UpdateAsync_WithExistingAccount_PreservesImmutableAccountState()
        {
            var paymentAccountId = Guid.Parse("9cd792b8-92b5-4e9a-a7eb-fd4de693ad46");
            var existingAccount = new PaymentAccount
            {
                Key = paymentAccountId,
                Agent = "Original agent",
                InitialBalance = 11.2m,
                Balance = 25.3m,
                Currency = "USD",
                Description = "Original description",
                Type = AccountTypes.Deposit
            };

            var documentClient = new FakePaymentAccountDocumentClient(existingAccount);
            var controller = CreateController(documentClient);

            var request = new UpdatePaymentAccountRequest
            {
                Balance = 150,
                AccountType = AccountTypes.Loan.Key,
                Agent = "Updated agent",
                Currency = "BYN",
                Description = "Updated description"
            };

            var result = await controller.UpdateAsync(paymentAccountId.ToString(), request);

            var updatedAccount = documentClient.GetStoredAccount(paymentAccountId);

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeTrue();
                result.Payload.Should().Be(paymentAccountId);
                updatedAccount.Key.Should().Be(paymentAccountId);
                updatedAccount.InitialBalance.Should().Be(11.2m);
                updatedAccount.Balance.Should().Be(25.3m);
                updatedAccount.Agent.Should().Be(request.Agent);
                updatedAccount.Currency.Should().Be(request.Currency);
                updatedAccount.Description.Should().Be(request.Description);
                updatedAccount.Type.Should().Be(AccountTypes.Loan);
            });
        }

        [Test]
        public async Task UpdateAsync_WithExistingAccount_KeepsAccountAddressableByOriginalKey()
        {
            var paymentAccountId = Guid.Parse("2c658882-2434-499b-933c-e4fd6a7095dc");
            var existingAccount = new PaymentAccount
            {
                Key = paymentAccountId,
                Agent = "Original agent",
                InitialBalance = 11.2m,
                Balance = 11.2m,
                Currency = "USD",
                Description = "Original description",
                Type = AccountTypes.Deposit
            };

            var documentClient = new FakePaymentAccountDocumentClient(existingAccount);
            var controller = CreateController(documentClient);

            var request = new UpdatePaymentAccountRequest
            {
                Balance = 150,
                AccountType = AccountTypes.Loan.Key,
                Agent = "Updated agent",
                Currency = "BYN",
                Description = "Updated description"
            };

            await controller.UpdateAsync(paymentAccountId.ToString(), request);

            var lookupResult = await documentClient.GetByIdAsync(paymentAccountId.ToString());

            Assert.Multiple(() =>
            {
                lookupResult.IsSucceeded.Should().BeTrue();
                lookupResult.Payload.Should().NotBeNull();
                lookupResult.Payload.Payload.Key.Should().Be(paymentAccountId);
            });
        }

        [Test]
        public async Task UpdateAsync_WithMissingPaymentAccount_ReturnsFailure()
        {
            var paymentAccountId = Guid.Parse("d90f4bb8-663a-4d01-a099-9709c0e81a9d");
            var documentClient = new FakePaymentAccountDocumentClient();
            var controller = CreateController(documentClient);

            var request = new UpdatePaymentAccountRequest
            {
                Balance = 100,
                AccountType = AccountTypes.Cash.Key,
                Agent = "Agent",
                Currency = "BYN",
                Description = "Description"
            };

            var result = await controller.UpdateAsync(paymentAccountId.ToString(), request);

            Assert.Multiple(() =>
            {
                result.IsSucceeded.Should().BeFalse();
                result.StatusMessage.Should().Contain(paymentAccountId.ToString());
            });
        }

        [Test]
        public async Task UpdateAsync_WithInvalidPaymentAccountId_ReturnsFailure()
        {
            var documentClient = new FakePaymentAccountDocumentClient();
            var controller = CreateController(documentClient);

            var request = new UpdatePaymentAccountRequest
            {
                Balance = 100,
                AccountType = AccountTypes.Cash.Key,
                Agent = "Agent",
                Currency = "BYN",
                Description = "Description"
            };

            var result = await controller.UpdateAsync("invalid", request);

            result.IsSucceeded.Should().BeFalse();
        }

        private static PaymentAccountsController CreateController(FakePaymentAccountDocumentClient documentClient)
        {
            return new PaymentAccountsController(
                Channel.CreateUnbounded<AccountRecord>(),
                null,
                documentClient,
                null);
        }

        private sealed class FakePaymentAccountDocumentClient : IPaymentAccountDocumentClient
        {
            private readonly Dictionary<Guid, PaymentAccountDocument> documents = [];

            public FakePaymentAccountDocumentClient(params PaymentAccount[] paymentAccounts)
            {
                foreach (var paymentAccount in paymentAccounts)
                {
                    documents[paymentAccount.Key] = new PaymentAccountDocument
                    {
                        Payload = paymentAccount
                    };
                }
            }

            public Task<Result<IReadOnlyCollection<PaymentAccountDocument>>> GetAsync()
            {
                return Task.FromResult(Result<IReadOnlyCollection<PaymentAccountDocument>>.Succeeded(documents.Values));
            }

            public Task<Result<PaymentAccountDocument>> GetByIdAsync(string paymentAccountId)
            {
                var paymentAccountGuid = Guid.Parse(paymentAccountId);

                documents.TryGetValue(paymentAccountGuid, out var document);

                return Task.FromResult(Result<PaymentAccountDocument>.Succeeded(document));
            }

            public Task<Result<Guid>> InsertOneAsync(PaymentAccount payload)
            {
                documents[payload.Key] = new PaymentAccountDocument
                {
                    Payload = payload
                };

                return Task.FromResult(Result<Guid>.Succeeded(payload.Key));
            }

            public Task<Result<Guid>> RemoveAsync(string paymentAccountId)
            {
                var paymentAccountGuid = Guid.Parse(paymentAccountId);

                documents.Remove(paymentAccountGuid);

                return Task.FromResult(Result<Guid>.Succeeded(paymentAccountGuid));
            }

            public Task<Result<Guid>> UpdateAsync(string requestPaymentAccountGuid, PaymentAccount paymentAccountForUpdate)
            {
                var paymentAccountGuid = Guid.Parse(requestPaymentAccountGuid);

                if (!documents.ContainsKey(paymentAccountGuid))
                {
                    return Task.FromResult(Result<Guid>.Failure());
                }

                documents[paymentAccountGuid] = new PaymentAccountDocument
                {
                    Payload = paymentAccountForUpdate
                };

                return Task.FromResult(Result<Guid>.Succeeded(paymentAccountGuid));
            }

            public PaymentAccount GetStoredAccount(Guid paymentAccountId)
            {
                return documents[paymentAccountId].Payload;
            }
        }
    }
}
