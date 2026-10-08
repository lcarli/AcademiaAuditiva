using System.Collections.Concurrent;
using AcademiaAuditiva.Interfaces;
using AcademiaAuditiva.Services.Audio.Processing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AcademiaAuditiva.IntegrationTests;

/// <summary>
/// <see cref="SignedInWebApplicationFactory"/> whose mixer records the plans it is asked
/// to mix and answers with <see cref="Clip"/>, so Explore requests never reach storage.
/// </summary>
public class ExploreWebApplicationFactory : SignedInWebApplicationFactory
{
    public const string Clip = "piano-audio-mixed/mix-test.wav";

    public RecordingMixer Mixer { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAudioMixerService>();
            services.AddSingleton<IAudioMixerService>(Mixer);
        });
    }

    public sealed class RecordingMixer : IAudioMixerService
    {
        public ConcurrentQueue<IReadOnlyList<MixInput>> Plans { get; } = new();

        public ConcurrentQueue<AudioProcessingPlan> ProcessingPlans { get; } = new();

        public Task<MixedAudio> MixAsync(IReadOnlyList<MixInput> inputs, CancellationToken cancellationToken = default)
        {
            Plans.Enqueue(inputs);
            return Task.FromResult(new MixedAudio("piano-audio-mixed", "mix-test.wav"));
        }

        public Task<MixedAudio> RenderAsync(AudioProcessingPlan plan, CancellationToken cancellationToken = default)
        {
            ProcessingPlans.Enqueue(plan);
            return Task.FromResult(new MixedAudio("piano-audio-mixed", "proc-test.wav"));
        }
    }
}
