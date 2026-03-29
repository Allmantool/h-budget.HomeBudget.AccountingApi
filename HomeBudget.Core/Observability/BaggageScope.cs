using System;

using OpenTelemetry;

namespace HomeBudget.Core.Observability
{
    public readonly struct BaggageScope : IDisposable
    {
        private readonly Baggage _previous;

        public BaggageScope(Baggage previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            Baggage.Current = _previous;
        }
    }
}
