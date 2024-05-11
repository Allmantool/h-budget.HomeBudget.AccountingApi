﻿using System;

using FluentAssertions;
using NUnit.Framework;

using HomeBudget.Accounting.Domain.Models;
using HomeBudget.Components.Operations.Builders;

namespace HomeBudget.Components.Operations.Tests.Builders
{
    [TestFixture]
    public class CrossAccountsTransferBuilderTests
    {
        private readonly CrossAccountsTransferBuilder _sut = new();

        [Test]
        public void Build_WhenRecipientOrSenderHaveNotBeenProvided_ThenNotSuccessfully()
        {
           var result = _sut.Build();

           result.IsSucceeded.Should().BeFalse();
        }

        [Test]
        public void Build_WhenRecipientAndSenderAreProvided_ThenSuccessfully()
        {
            var result = _sut
                .WithRecipient(new PaymentOperation
                {
                    PaymentAccountId = Guid.Parse("bfdc41fb-5203-4d22-93bf-a7bc55b99f0f"),
                })
                .WithSender(new PaymentOperation
                {
                    PaymentAccountId = Guid.Parse("54095569-8e60-4500-b166-7b761dbe3103"),
                })
                .Build();

            result.IsSucceeded.Should().BeTrue();
        }
    }
}
