using System;
using System.Linq;
using System.Threading.Tasks;

using FluentAssertions;
using Moq;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Enumerations;
using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Accounts.Clients.Interfaces;
using HomeBudget.Components.Accounts.Models;
using HomeBudget.Components.Operations.Builders;
using HomeBudget.Core.Models;

namespace HomeBudget.Components.Operations.Tests.Builders
{
    [TestFixture]
    public class CrossAccountsTransferBuilderTests
    {
        private CrossAccountsTransferBuilder _sut;

        private Mock<IPaymentAccountDocumentClient> _accountDocumentClientMock;

        [SetUp]
        public void Setup()
        {
            _accountDocumentClientMock = new Mock<IPaymentAccountDocumentClient>();

            _accountDocumentClientMock
                .Setup(cl => cl.GetByIdAsync(It.IsAny<string>()))
                .ReturnsAsync(Result<PaymentAccountDocument>.Succeeded(new PaymentAccountDocument
                {
                    Payload = new PaymentAccount
                    {
                        Description = "any description",
                        Agent = "any agent",
                        Currency = CurrencyTypes.Pln.Name
                    }
                }));

            _accountDocumentClientMock
                .Setup(cl => cl.GetByIdAsync("bfdc41fb-5203-4d22-93bf-a7bc55b99f0f"))
                .ReturnsAsync(Result<PaymentAccountDocument>.Succeeded(new PaymentAccountDocument
                {
                    Payload = new PaymentAccount
                    {
                        Description = "recipient description",
                        Agent = "recipient agent",
                        Currency = CurrencyTypes.Byn.Name
                    }
                }));

            _accountDocumentClientMock
                .Setup(cl => cl.GetByIdAsync("54095569-8e60-4500-b166-7b761dbe3103"))
                .ReturnsAsync(Result<PaymentAccountDocument>.Succeeded(new PaymentAccountDocument
                {
                    Payload = new PaymentAccount
                    {
                        Description = "sender description",
                        Agent = "sender agent",
                        Currency = CurrencyTypes.Usd.Name
                    }
                }));

            _sut = new(_accountDocumentClientMock.Object);
        }

        [Test]
        public async Task BuildAsync_WhenRecipientOrSenderHaveNotBeenProvided_ThenNotSuccessfully()
        {
            var result = await _sut.BuildAsync();

            result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public async Task BuildAsync_WhenRecipientAndSenderAreProvided_ThenSuccessfully()
        {
            var result = await _sut
                .WithRecipient(new FinancialTransaction
                {
                    PaymentAccountId = Guid.Parse("bfdc41fb-5203-4d22-93bf-a7bc55b99f0f"),
                })
                .WithSender(new FinancialTransaction
                {
                    PaymentAccountId = Guid.Parse("54095569-8e60-4500-b166-7b761dbe3103"),
                })
                .BuildAsync();

            result.IsSucceeded.Should().BeTrue();
        }

        [Test]
        public async Task BuildAsync_WithExplicitTransferId_ThenExpectedTransferIdForAllOperations()
        {
            var testTransferId = Guid.Parse("1938c4bd-6105-4979-9751-e08e0380aab7");

            var result = await _sut
                .WithRecipient(new FinancialTransaction
                {
                    PaymentAccountId = Guid.Parse("bfdc41fb-5203-4d22-93bf-a7bc55b99f0f"),
                })
                .WithSender(new FinancialTransaction
                {
                    PaymentAccountId = Guid.Parse("54095569-8e60-4500-b166-7b761dbe3103"),
                })
                .WithTransferId(testTransferId)
                .BuildAsync();

            result.Payload.PaymentOperations.All(op => op.Key.Equals(testTransferId)).Should().BeTrue();
        }

        [Test]
        public async Task BuildAsync_WhenStandardBuild_ThenAlightWithRules()
        {
            var result = await _sut
                .WithRecipient(new FinancialTransaction
                {
                    PaymentAccountId = Guid.Parse("bfdc41fb-5203-4d22-93bf-a7bc55b99f0f"),
                })
                .WithSender(new FinancialTransaction
                {
                    PaymentAccountId = Guid.Parse("54095569-8e60-4500-b166-7b761dbe3103"),
                })
                .BuildAsync();

            var transfer = result.Payload;
            var operations = transfer.PaymentOperations;

            var sender = operations.First(op => op.PaymentAccountId.ToString() == "54095569-8e60-4500-b166-7b761dbe3103");
            var recipient = operations.First(op => op.PaymentAccountId.ToString() == "bfdc41fb-5203-4d22-93bf-a7bc55b99f0f");

            Assert.Multiple(() =>
            {
                sender.Key = recipient.Key;

                sender.Key.Should().Be(transfer.Key);
                recipient.Key.Should().Be(transfer.Key);

                recipient.PaymentAccountId.Should().NotBe(sender.PaymentAccountId);
            });
        }

        [Test]
        public async Task BuildAsync_WhenStandardTransfer_ThenCommentsShouldBeAlignWithExpectations()
        {
            var result = await _sut
                .WithRecipient(new FinancialTransaction
                {
                    PaymentAccountId = Guid.Parse("bfdc41fb-5203-4d22-93bf-a7bc55b99f0f"),
                })
                .WithSender(new FinancialTransaction
                {
                    PaymentAccountId = Guid.Parse("54095569-8e60-4500-b166-7b761dbe3103"),
                })
                .BuildAsync();

            var transfer = result.Payload;
            var operations = transfer.PaymentOperations;

            var sender = operations.First(op => op.PaymentAccountId.ToString() == "54095569-8e60-4500-b166-7b761dbe3103");
            var recipient = operations.First(op => op.PaymentAccountId.ToString() == "bfdc41fb-5203-4d22-93bf-a7bc55b99f0f");

            Assert.Multiple(() =>
            {
                sender.Comment.Should().Be("Transfer to recipient agent | recipient description ($Byn)");
                recipient.Comment.Should().Be("Transfer from sender agent | sender description ($Usd)");
            });
        }
    }
}
