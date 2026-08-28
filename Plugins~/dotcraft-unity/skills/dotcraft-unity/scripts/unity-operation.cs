using Newtonsoft.Json;

// Starts or updates a durable Unity automation operation.
// Args:
//   action: compile | reload | begin | report | complete | fail | read | path
//   name: operation label for compile, reload, or begin
//   id: caller-generated operation id for compile/reload, or existing id for other actions
//   phase/message: checkpoint data
//   cleanBuildCache: optional bool for compile; keep false unless explicitly required

var action = ((string)Args["action"] ?? "").Trim().ToLowerInvariant();
var name = (string)Args["name"];
var id = (string)Args["id"];
var phase = (string)Args["phase"];
var message = (string)Args["message"];

object result;
switch (action)
{
    case "compile":
        result = DcuLongRunningOperation.RequestScriptCompilation(
            string.IsNullOrWhiteSpace(name) ? "script-compilation" : name,
            (bool?)Args["cleanBuildCache"] ?? false,
            id);
        break;
    case "reload":
        result = DcuLongRunningOperation.RequestDomainReload(
            string.IsNullOrWhiteSpace(name) ? "domain-reload" : name,
            id);
        break;
    case "begin":
        result = DcuLongRunningOperation.Begin(name);
        break;
    case "report":
        result = DcuLongRunningOperation.Report(id, phase, message);
        break;
    case "complete":
        result = DcuLongRunningOperation.Complete(id, message);
        break;
    case "fail":
        result = DcuLongRunningOperation.Fail(id, message);
        break;
    case "read":
        result = DcuLongRunningOperation.Read(id);
        break;
    case "path":
        result = DcuLongRunningOperation.GetStatePath(id);
        break;
    default:
        return "Unknown action. Use compile, reload, begin, report, complete, fail, read, or path.";
}

return result is string text ? text : JsonConvert.SerializeObject(result);
