using System;
using System.IO;
using System.Text;

namespace HomeBudget.Accounting.Api.IntegrationTests.Release
{
    internal sealed class ReleaseConsoleCapture : IDisposable
    {
        private readonly TextWriter _originalOutput;
        private readonly TextWriter _originalError;
        private readonly StreamWriter _output;
        private readonly StreamWriter _error;
        private bool _isDisposed;

        private ReleaseConsoleCapture(string outputPath, string errorPath)
        {
            _originalOutput = Console.Out;
            _originalError = Console.Error;
            _output = CreateWriter(outputPath);
            _error = CreateWriter(errorPath);
            Console.SetOut(TextWriter.Synchronized(_output));
            Console.SetError(TextWriter.Synchronized(_error));
        }

        public static ReleaseConsoleCapture Start(string outputPath, string errorPath)
        {
            return new ReleaseConsoleCapture(outputPath, errorPath);
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            Console.SetOut(_originalOutput);
            Console.SetError(_originalError);
            _output.Dispose();
            _error.Dispose();
            _isDisposed = true;
        }

        private static StreamWriter CreateWriter(string path)
        {
            return new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
        }
    }
}
