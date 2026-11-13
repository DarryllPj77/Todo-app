using System.Text.Json.Serialization;
using TodoApp.Domain.Recurrence;

namespace TodoApp.Api.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecurrenceRuleRequest([property: JsonRequired] RepeatRule? Rule);
