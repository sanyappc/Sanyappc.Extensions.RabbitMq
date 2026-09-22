using System.Diagnostics.Metrics;
using System.Threading.Channels;

namespace Sanyappc.Extensions.RabbitMq.Tests;

// Reads an instrument the way the exporter does: what is not recorded here never reaches a dashboard.
internal sealed class Measurements : IDisposable
{
    private readonly MeterListener listener = new();
    private readonly Channel<Measurement> measured = Channel.CreateUnbounded<Measurement>();
    private readonly string instrumentName;
    private readonly string tagKey;
    private readonly object tagValue;

    public Measurements(string instrumentName, string tagKey, object tagValue)
    {
        this.instrumentName = instrumentName;
        this.tagKey = tagKey;
        this.tagValue = tagValue;
        listener.InstrumentPublished = Enable;
        listener.SetMeasurementEventCallback<double>(Record);
        listener.Start();
    }

    private void Enable(Instrument instrument, MeterListener meterListener)
    {
        if (instrument.Meter.Name != RabbitMqTelemetry.MeterName)
            return;

        if (instrument.Name != instrumentName)
            return;

        meterListener.EnableMeasurementEvents(instrument);
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags, object? state)
    {
        Dictionary<string, object?> copied = [];
        foreach (KeyValuePair<string, object?> tag in tags)
            copied[tag.Key] = tag.Value;

        if (!Equals(copied.GetValueOrDefault(tagKey), tagValue))
            return;

        measured.Writer.TryWrite(new Measurement(value, copied));
    }

    public Task<Measurement> NextAsync(TimeSpan patience, CancellationToken cancellationToken) =>
        measured.Reader.ReadAsync(cancellationToken).AsTask().WaitAsync(patience, cancellationToken);

    public void Dispose() => listener.Dispose();
}
