using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TodoApp.Api.Contracts;
using TodoApp.Domain.Entities;
using TodoApp.Domain.Enums;
using TodoApp.Domain.Recurrence;
using TodoApp.Domain.Sync;
using TodoApp.Infrastructure.Services;
using TodoApp.Maui.Models;
using TodoApp.Maui.Services;

namespace TodoApp.Tests;

public sealed partial class PostgresAuthenticationTests
{
    private static RepeatRule Monthly(string anchor = "2026-01-31") => new(RepeatFrequency.Monthly,1,DateOnly.Parse(anchor));
    private Task<HttpResponseMessage> Repeat(Guid id, RepeatRule? rule) => client.PutAsJsonAsync($"/api/tasks/{id}/recurrence",new {rule});
    private Task<HttpResponseMessage> CompleteRepeat(Guid id, RepeatState expected) => client.PostAsJsonAsync($"/api/tasks/{id}/occurrences/complete",expected);
    private async Task<TaskResponse> RepeatingTask(string title = "Repeat")
    {
        var task = await HierarchyCreate(title); using var response = await Repeat(task.Id,Monthly()); Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!;
    }
    [PostgresFact]
    public async Task Recurrence_RestRetriesAndConcurrentCompletionsAdvanceOnceAndKeepOneTask()
    {
        As(await Register("recurrence-rest@example.invalid")); var task = await RepeatingTask(); var expected=task.Recurrence!;
        var replies=await Task.WhenAll(CompleteRepeat(task.Id,expected),CompleteRepeat(task.Id,expected));
        try
        {
            foreach(var reply in replies) {Assert.Equal(HttpStatusCode.OK,reply.StatusCode); Assert.Equal(1,(await reply.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!.Recurrence!.Occurrence);}
        }
        finally {foreach(var reply in replies)reply.Dispose();}
        var after=(await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{task.Id}",ClientHarness.JsonOptions))!;
        Assert.Equal(new DateOnly(2026,2,28),after.DueDate); Assert.False(after.IsCompleted); Assert.Equal(task.CreatedAt,after.CreatedAt); Assert.Equal(task.Tags,after.Tags);
        var revision=Assert.Single((await Download()).Changes).Revision;
        using var retry=await CompleteRepeat(task.Id,expected); Assert.Equal(HttpStatusCode.OK,retry.StatusCode); Assert.Empty((await Download(revision)).Changes);
        using var second=await CompleteRepeat(task.Id,after.Recurrence!); Assert.Equal(new DateOnly(2026,3,31),(await second.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!.DueDate);
        await using var db=Context(); Assert.Single(await db.Tasks.ToListAsync()); Assert.Equal(2,(await db.SyncRecords.SingleAsync()).RecurrenceFloor);
    }
    [PostgresFact]
    public async Task Recurrence_RestProtectsOwnershipValidatesBodiesAndPreservesOmittedState()
    {
        using var anonymous=await Repeat(Guid.NewGuid(),Monthly()); Assert.Equal(HttpStatusCode.Unauthorized,anonymous.StatusCode);
        var alice=await Register("recurrence-alice@example.invalid"); var bob=await Register("recurrence-bob@example.invalid"); As(alice); var task=await RepeatingTask(); As(bob);
        using var hidden=await CompleteRepeat(task.Id,task.Recurrence!); Assert.Equal(HttpStatusCode.NotFound,hidden.StatusCode); using var foreign=await Repeat(task.Id,null); Assert.Equal(HttpStatusCode.NotFound,foreign.StatusCode); As(alice);
        foreach(var body in new[]{"{}","null","{\"rule\":{}}","{\"rule\":{\"frequency\":1,\"interval\":1,\"anchorDate\":\"2026-01-31\"}}","{\"rule\":null,\"ownerId\":\""+bob.User.Id+"\"}"})
        {using var rejected=await client.PutAsync($"/api/tasks/{task.Id}/recurrence",new StringContent(body,System.Text.Encoding.UTF8,"application/json")); Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);}
        using var invalid=await Repeat(task.Id,Monthly() with{Interval=0}); Assert.Equal(HttpStatusCode.Conflict,invalid.StatusCode);
        using var legacy=await client.PutAsJsonAsync($"/api/tasks/{task.Id}",new{title="Legacy edit",dueDate=task.DueDate}); Assert.Equal(HttpStatusCode.OK,legacy.StatusCode); Assert.Equal(task.Recurrence,(await legacy.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!.Recurrence);
        using var ordinary=await client.PutAsJsonAsync($"/api/tasks/{task.Id}",new{title="Wrong",dueDate=task.DueDate,isCompleted=true,categoryName="No partial category"}); Assert.Equal(HttpStatusCode.Conflict,ordinary.StatusCode);
        using var dateEdit=await client.PutAsJsonAsync($"/api/tasks/{task.Id}",new{title="Wrong",dueDate=new DateOnly(2026,2,1)}); Assert.Equal(HttpStatusCode.Conflict,dateEdit.StatusCode);
        Assert.Equal("Legacy edit",(await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{task.Id}",ClientHarness.JsonOptions))!.Title);
        using var changed=await Repeat(task.Id,Monthly("2026-04-30")); Assert.Equal(HttpStatusCode.OK,changed.StatusCode);
        using var stale=await CompleteRepeat(task.Id,task.Recurrence!); Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        using var stop=await Repeat(task.Id,null); Assert.Equal(HttpStatusCode.OK,stop.StatusCode);
        using var finish=await client.PutAsJsonAsync($"/api/tasks/{task.Id}",new{title="Finished",dueDate=new DateOnly(2026,4,30),isCompleted=true}); Assert.Equal(HttpStatusCode.OK,finish.StatusCode);
        await using var db=Context(); Assert.Empty(await db.Categories.ToListAsync());
    }
    [PostgresFact]
    public async Task Recurrence_DependencyPolicyPreventsRepeatingPrerequisitesAndGatesAdvance()
    {
        As(await Register("recurrence-dependencies@example.invalid")); var first=await HierarchyCreate("First"); var second=await HierarchyCreate("Second");
        using var linked=await Dependencies(second.Id,first.Id); Assert.Equal(HttpStatusCode.OK,linked.StatusCode);
        using var blockedEnable=await Repeat(first.Id,Monthly()); Assert.Equal(HttpStatusCode.Conflict,blockedEnable.StatusCode);
        using var enabled=await Repeat(second.Id,Monthly()); var repeating=(await enabled.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!;
        using var blockedComplete=await CompleteRepeat(second.Id,repeating.Recurrence!); Assert.Equal(HttpStatusCode.Conflict,blockedComplete.StatusCode);
        using var finishFirst=await client.PutAsJsonAsync($"/api/tasks/{first.Id}",new{title="First",isCompleted=true}); Assert.Equal(HttpStatusCode.OK,finishFirst.StatusCode);
        using var complete=await CompleteRepeat(second.Id,repeating.Recurrence!); Assert.Equal(HttpStatusCode.OK,complete.StatusCode);
        var third=await HierarchyCreate("Third"); using var invalid=await Dependencies(third.Id,second.Id); Assert.Equal(HttpStatusCode.Conflict,invalid.StatusCode);
    }
    [PostgresFact]
    public async Task Recurrence_MigrationPreservesPlanningFieldsAndAddsCounterMetadata()
    {
        var account=await Register("recurrence-migration@example.invalid"); var parent=Guid.NewGuid(); var child=Guid.NewGuid(); await using var db=Context(); await db.GetService<IMigrator>().MigrateAsync("AddTaskPlanning");
        var raw=DateTimeOffset.UtcNow; var now=new DateTimeOffset(raw.UtcTicks-raw.UtcTicks%10,TimeSpan.Zero); var prerequisites=JsonSerializer.Serialize(Prerequisites(parent));
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Tasks" ("Id","Title","IsCompleted","Priority","CreatedAt","UpdatedAt","OwnerId","Tags","ParentTaskId","Prerequisites","Quadrant")
            VALUES ({parent},'Parent',false,1,{now},{now},{account.User.Id},'[]',NULL,'[]',0),
                ({child},'Child',false,2,{now},{now},{account.User.Id},'["work"]',{parent},{prerequisites},2);
            """);
        await db.Database.MigrateAsync(); var restored=await db.Tasks.AsNoTracking().SingleAsync(t=>t.Id==child);
        Assert.Equal(RepeatState.None,restored.Recurrence); Assert.Equal(EisenhowerQuadrant.ImportantNotUrgent,restored.Quadrant); Assert.Equal(Prerequisites(parent),restored.Prerequisites); Assert.Equal(parent,restored.ParentTaskId); Assert.Equal(TagSet.Parse("work"),restored.Tags);
        Assert.Equal(now,restored.CreatedAt); Assert.Equal(now,restored.UpdatedAt); Assert.False(db.Database.HasPendingModelChanges()); Assert.Equal(8,(await db.Database.GetAppliedMigrationsAsync()).Count());
        As(account); Assert.Equal(6,(await Download()).ProtocolVersion); Assert.All(await db.SyncRecords.AsNoTracking().ToListAsync(),r=>Assert.Equal(0,r.RecurrenceFloor));
    }
    [PostgresFact]
    public async Task Recurrence_SyncAdvancesReplayReceiptsAndRejectSkippedCountersWithoutPartialWrites()
    {
        As(await Register("recurrence-sync@example.invalid")); var repeat=new RepeatState(Monthly(),0,0);
        var original=await Applied(new(Guid.NewGuid(),Guid.NewGuid(),0,false,new("Task",DueDate:RecurrenceRules.DueDate(repeat),Recurrence:repeat)));
        var next=RecurrenceRules.Complete(repeat,repeat); var mutation=new SyncMutation(Guid.NewGuid(),original.Current.TaskId,original.Current.Revision,false,original.Current.Task! with{Recurrence=next,DueDate=RecurrenceRules.DueDate(next)});
        var first=await Applied(mutation); Assert.False(first.Conflict); Assert.Equal(1,first.Current.RecurrenceFloor); Assert.Equal(first,await Applied(mutation));
        var secondState=RecurrenceRules.Complete(next,next); var second=await Applied(mutation with{OperationId=Guid.NewGuid(),BaseRevision=first.Current.Revision,Task=mutation.Task! with{Recurrence=secondState,DueDate=RecurrenceRules.DueDate(secondState)}}); Assert.False(second.Conflict);
        Assert.Equal(first,await Applied(mutation)); var jump=secondState with{Occurrence=4};
        var rejected=await Applied(new(Guid.NewGuid(),mutation.TaskId,second.Current.Revision,false,second.Current.Task! with{Title="Wrong",CategoryName="No partial category",Recurrence=jump,DueDate=RecurrenceRules.DueDate(jump)}));
        Assert.True(rejected.Conflict); Assert.NotNull(rejected.ConflictReason); Assert.Equal(second.Current,rejected.Current);
        await using var db=Context(); Assert.Empty(await db.Categories.ToListAsync()); Assert.Equal(2,(await db.Tasks.SingleAsync()).Recurrence.Occurrence);
    }
    [PostgresFact]
    public async Task Recurrence_DeleteRestoreRetainsCounterAndOldCompletionCannotAdvanceAgain()
    {
        As(await Register("recurrence-restore@example.invalid")); var task=await RepeatingTask(); var original=task.Recurrence!;
        using var completed=await CompleteRepeat(task.Id,original); Assert.Equal(HttpStatusCode.OK,completed.StatusCode);
        using var removed=await client.DeleteAsync($"/api/tasks/{task.Id}"); Assert.Equal(HttpStatusCode.NoContent,removed.StatusCode); var deleted=Assert.Single((await Download()).Changes); Assert.True(deleted.Deleted); Assert.Equal(1,deleted.RecurrenceFloor);
        var stale=await Applied(new(Guid.NewGuid(),task.Id,deleted.Revision,false,new("Restore",DueDate:RecurrenceRules.DueDate(original),Recurrence:original))); Assert.True(stale.Conflict); Assert.Equal(deleted,stale.Current);
        var repaired=new RepeatState(Monthly("2026-02-28"),1,1); var restored=await Applied(new(Guid.NewGuid(),task.Id,deleted.Revision,false,new("Restore",DueDate:RecurrenceRules.DueDate(repaired),Recurrence:repaired))); Assert.False(restored.Conflict); Assert.Equal(1,restored.Current.RecurrenceFloor);
        using var retry=await CompleteRepeat(task.Id,original); Assert.Equal(HttpStatusCode.OK,retry.StatusCode); Assert.Equal(repaired,(await retry.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!.Recurrence); Assert.Empty((await Download(restored.Current.Revision)).Changes);
    }
    [PostgresFact]
    public async Task Recurrence_LegacyPlanningReceiptReplayCannotClobberAnAdvancedRule()
    {
        As(await Register("recurrence-receipt@example.invalid")); var legacy=new SyncMutation(Guid.NewGuid(),Guid.NewGuid(),0,false,new("Legacy",Quadrant:EisenhowerQuadrant.Neither)); Assert.DoesNotContain("Recurrence",JsonSerializer.Serialize(legacy));
        var original=await Applied(legacy); var old=original with{Current=original.Current with{Task=original.Current.Task! with{Recurrence=null}}};
        await using(var db=Context()){var receipt=await db.SyncReceipts.SingleAsync();receipt.Response=JsonSerializer.Serialize(old);await db.SaveChangesAsync();}
        using var enabled=await Repeat(legacy.TaskId,Monthly()); var task=(await enabled.Content.ReadFromJsonAsync<TaskResponse>(ClientHarness.JsonOptions))!;
        using var completed=await CompleteRepeat(task.Id,task.Recurrence!); Assert.Equal(HttpStatusCode.OK,completed.StatusCode); Assert.Equal(old,await Applied(legacy));
        Assert.Equal(1,(await client.GetFromJsonAsync<TaskResponse>($"/api/tasks/{task.Id}",ClientHarness.JsonOptions))!.Recurrence!.Occurrence);
    }
    [PostgresFact]
    public async Task Recurrence_TwoOfflineCompletionsConvergeToOneAdvanceThenAllowTheNext()
    {
        var account=await Register("recurrence-replicas@example.invalid"); var settings=new MemoryClientSettings{ApiBaseUrl="https://localhost/"}; var session=new ClientSession(settings,new MemoryTokenStore());
        await session.SaveAsync(new ApiAuthResponse(account.AccessToken,account.TokenType,account.ExpiresAt,new(account.User.Id,account.User.Email)),new Uri(settings.ApiBaseUrl),session.Version); var api=new TodoApiClient(client,session);
        var directory=Path.Combine(Path.GetTempPath(),"todoapp_recurrence_replicas_"+Guid.NewGuid().ToString("N")); var a=new SqliteSyncStore(Path.Combine(directory,"a.db3"));var b=new SqliteSyncStore(Path.Combine(directory,"b.db3"));
        var scope=SqliteSyncStore.Partition(settings.ApiBaseUrl,account.User.Id);var id=Guid.NewGuid();var engineA=new SyncEngine(session,api,a);var engineB=new SyncEngine(session,api,b);var repeat=new RepeatState(Monthly(),0,0);
        try
        {
            await a.QueueAsync(scope,id,new("Task",DueDate:RecurrenceRules.DueDate(repeat),Recurrence:repeat));await engineA.RunAsync();await engineB.RunAsync();
            var rowA=(await a.ReadAsync(scope)).Rows[id];var rowB=(await b.ReadAsync(scope)).Rows[id];
            await a.CompleteOccurrenceAsync(scope,id,rowA.Remote!.Revision,rowA.Local!);await b.CompleteOccurrenceAsync(scope,id,rowB.Remote!.Revision,rowB.Local!);await engineA.RunAsync();await engineB.RunAsync();
            rowB=(await b.ReadAsync(scope)).Rows[id];Assert.True(rowB.Conflict);Assert.Equal(1,rowB.Local!.Recurrence!.Occurrence);Assert.Equal(1,rowB.Remote!.Task!.Recurrence!.Occurrence);
            await b.SetRecurrenceRuleAsync(scope,id,Monthly(),rowB.Remote.Revision,rowB.Local,true);await engineB.RunAsync();await engineA.RunAsync();
            Assert.Equal(1,(await a.ReadAsync(scope)).Rows[id].Local!.Recurrence!.Occurrence);rowB=(await b.ReadAsync(scope)).Rows[id];Assert.False(rowB.Conflict);Assert.Null(rowB.Pending);
            await b.CompleteOccurrenceAsync(scope,id,rowB.Remote!.Revision,rowB.Local!);await engineB.RunAsync();await engineA.RunAsync();
            Assert.Equal(2,(await a.ReadAsync(scope)).Rows[id].Local!.Recurrence!.Occurrence);Assert.Equal(new DateOnly(2026,3,31),(await a.ReadAsync(scope)).Rows[id].Local!.DueDate);
            await using var db=Context();Assert.Single(await db.Tasks.ToListAsync());
        }
        finally{if(Directory.Exists(directory))Directory.Delete(directory,true);}
    }
}
