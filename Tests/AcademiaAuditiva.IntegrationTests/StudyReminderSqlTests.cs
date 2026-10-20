using AcademiaAuditiva.Data;
using AcademiaAuditiva.Models;
using AcademiaAuditiva.Resources;
using AcademiaAuditiva.Services.Push;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AcademiaAuditiva.IntegrationTests;

[Collection(RealSqlServerCollection.Name)]
public sealed class StudyReminderSqlTests(RealSqlServerFixture fixture)
{
    [RealSqlFact]
    public async Task ReplicasClaimTheSameDeviceOnlyOnceAndAccountDeletionCascades()
    {
        await using (var setup = fixture.CreateContext())
        {
            await setup.Database.MigrateAsync();
            var user = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(), FirstName = "Push", LastName = "Test", Language = "en-US"
            };
            setup.Add(user);
            setup.StudyPushSubscriptions.Add(new StudyPushSubscription
            {
                UserId = user.Id, EndpointHash = Guid.NewGuid().ToString(),
                Endpoint = "https://web.push.apple.com/sql", TimeZoneId = "UTC", MinuteOfDay = 0
            });
            await setup.SaveChangesAsync();
            _userId = user.Id;
        }
        var sender = new BlockingSender();
        var clock = new AnswerTimeTests.ManualClock(new DateTimeOffset(2032, 1, 1, 20, 0, 0, TimeSpan.Zero));
        using var localizers = new ServiceCollection().AddLogging().AddLocalization().BuildServiceProvider();
        await using var firstDb = fixture.CreateContext();
        await using var secondDb = fixture.CreateContext();
        var first = Dispatcher(firstDb).RunAsync(default);
        await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Dispatcher(secondDb).RunAsync(default);
        sender.Calls.Should().Be(1, "the persisted lease excludes other replicas while a send is in flight");
        sender.Release.TrySetResult();
        await first;
        await Dispatcher(secondDb).RunAsync(default);
        sender.Calls.Should().Be(1, "the sent local day survives a new dispatcher run");
        await using var verify = fixture.CreateContext();
        (await verify.StudyPushSubscriptions.SingleAsync(s => s.UserId == _userId))
            .LastReminderDate.Should().Be(new DateOnly(2032, 1, 1));
        var userToDelete = await verify.Users.SingleAsync(u => u.Id == _userId);
        verify.Remove(userToDelete);
        await verify.SaveChangesAsync();
        (await verify.StudyPushSubscriptions.AnyAsync(s => s.UserId == _userId)).Should().BeFalse();

        StudyReminderDispatcher Dispatcher(ApplicationDbContext db) => new(db, sender, clock,
            localizers.GetRequiredService<IStringLocalizer<SharedResources>>(),
            Options.Create(new WebPushOptions { Enabled = true }), NullLogger<StudyReminderDispatcher>.Instance);
    }

    private string _userId = "";

    private sealed class BlockingSender : IStudyPushSender
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
        public async Task<bool> SendAsync(StudyPushSubscription subscription, string payload, CancellationToken ct)
        {
            if (Interlocked.Increment(ref Calls) != 1) return true;
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return true;
        }
    }
}
