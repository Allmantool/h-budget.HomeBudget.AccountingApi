using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using MediatR;

using HomeBudget.Core.Models;
using HomeBudget.Core.Validation;

namespace HomeBudget.Accounting.Api.Tests
{
    [TestFixture]
    public class ValidationBehaviorTests
    {
        [Test]
        public async Task Handle_WhenValidationFails_ShouldReturnFailureBeforeHandler()
        {
            var handlerCalled = false;
            var behavior = new ValidationBehavior<TestCommand, Result<Guid>>(
                [new TestCommandValidator("Amount must not be zero")]);

            var result = await behavior.Handle(
                new TestCommand(),
                _ =>
                {
                    handlerCalled = true;
                    return Task.FromResult(Result<Guid>.Succeeded(Guid.NewGuid()));
                },
                CancellationToken.None);

            result.IsSucceeded.Should().BeFalse();
            result.StatusMessage.Should().Contain("Validation failed");
            handlerCalled.Should().BeFalse();
        }

        private sealed class TestCommand : IRequest<Result<Guid>>
        {
        }

        private sealed class TestCommandValidator(string failure)
            : IRequestValidator<TestCommand>
        {
            public IReadOnlyCollection<string> Validate(TestCommand request)
            {
                return [failure];
            }
        }
    }
}
