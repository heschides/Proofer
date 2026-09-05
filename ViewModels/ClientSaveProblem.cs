using System.Net;
using Microsoft.EntityFrameworkCore;
using Sati.Data;
using Sati.Data.Cloud;

namespace Sati.ViewModels;

public enum ClientSaveStage
{
    LoadingSettings,
    PreparingRecord,
    SavingRecord,
    RefreshingAfterSave
}

public sealed record ClientSaveProblem(
    string Title,
    string Message,
    bool SaveStatusUnknown)
{
    public static ClientSaveProblem RefreshFailure(
        IEnumerable<string> failedAreas,
        bool clientWasJustSaved,
        string supportReference)
    {
        var areas = string.Join(
            ", ",
            failedAreas.Where(area => !string.IsNullOrWhiteSpace(area))
                .Distinct(StringComparer.OrdinalIgnoreCase));
        var saved = clientWasJustSaved
            ? "The new client and its related forms, lifecycle history, and audit entry were saved."
            : "No saved client data was changed by this read-only refresh.";
        return new ClientSaveProblem(
            clientWasJustSaved ? "Client Saved; Some Details Need Refresh" : "Client Details Need Refresh",
            $"WHAT WAS SAVED\n{saved}\n\nWHAT WENT WRONG\nSati could not refresh: {areas}.\n\nBEST FIX\nSelect the client again or refresh the Clients page. Do not add the client again.\n\nSupport reference: {supportReference}",
            SaveStatusUnknown: false);
    }

    public static ClientSaveProblem Validation(IEnumerable<string> messages, bool creating)
    {
        var details = string.Join(
            " ",
            messages.Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct(StringComparer.Ordinal));
        return Build(
            creating,
            saveStatusUnknown: false,
            problem: string.IsNullOrWhiteSpace(details)
                ? "One or more client fields are incomplete or invalid."
                : details,
            bestFix: creating
                ? "Correct the listed client details, then choose Add Client again."
                : "Correct the listed client details, then choose Save Changes again.");
    }

    public static ClientSaveProblem FromException(
        Exception exception,
        ClientSaveStage stage,
        bool creating,
        string supportReference)
    {
        if (stage == ClientSaveStage.RefreshingAfterSave)
        {
            return new ClientSaveProblem(
                creating ? "Client Saved; Screen Needs Refresh" : "Client Changes Saved; Screen Needs Refresh",
                $"WHAT WAS SAVED\n{(creating ? "The new client was saved." : "The client changes were saved.")}\n\nWHAT WENT WRONG\nSati could not finish updating the screen after saving.\n\nBEST FIX\nClose and reopen the Clients page to reload the saved details. Do not repeat the save.\n\nSupport reference: {supportReference}",
                SaveStatusUnknown: false);
        }

        if (exception is PersonValidationException validation)
        {
            return Validation(
                validation.Errors.Values.SelectMany(messages => messages),
                creating);
        }

        if (stage == ClientSaveStage.LoadingSettings)
        {
            return Build(
                creating,
                saveStatusUnknown: false,
                problem: "Sati could not load the agency's form-deadline settings, so it stopped before building the client record.",
                bestFix: "Close and reopen Sati so database updates and settings can reload. If this happens again, give support the reference below.",
                supportReference);
        }

        if (exception is CloudConnectivityException connectivity)
        {
            return connectivity.RequestWasDefinitelyNotSent
                ? Build(
                    creating,
                    saveStatusUnknown: false,
                    problem: connectivity.Message,
                    bestFix: "Restore the internet or DNS connection, then try Add Client again.",
                    supportReference)
                : Build(
                    creating,
                    saveStatusUnknown: true,
                    problem: connectivity.Message,
                    bestFix: "Refresh the client list before retrying. If the client appears, do not add it again; if it does not appear, retry once after the connection is stable.",
                    supportReference);
        }

        if (exception is CloudApiException cloud)
        {
            var correlation = string.IsNullOrWhiteSpace(cloud.CorrelationId)
                ? supportReference
                : cloud.CorrelationId;
            return cloud.StatusCode switch
            {
                HttpStatusCode.BadRequest => Build(
                    creating,
                    false,
                    $"The Demo server rejected these client details: {cloud.Message}",
                    "Correct the listed details, then choose Add Client again.",
                    correlation),
                HttpStatusCode.Unauthorized => Build(
                    creating,
                    false,
                    cloud.Message,
                    "Sign in again, reopen Clients, and retry the save.",
                    correlation),
                HttpStatusCode.Forbidden => Build(
                    creating,
                    false,
                    cloud.Message,
                    "Ask an agency Admin to verify your account and caseload permissions before retrying.",
                    correlation),
                HttpStatusCode.TooManyRequests => Build(
                    creating,
                    false,
                    cloud.Message,
                    "Wait for the stated interval, then retry once.",
                    correlation),
                _ => Build(
                    creating,
                    true,
                    $"The Demo server did not confirm the save. {cloud.Message}",
                    "Refresh the client list before retrying. If the client is absent and the problem persists, give support the reference below.",
                    correlation)
            };
        }

        if (exception is PersonPersistenceException or DbUpdateException)
        {
            return Build(
                creating,
                saveStatusUnknown: false,
                problem: "The database rejected the transaction. The client record and its related forms, history, and audit entry were rolled back together.",
                bestFix: "Close and reopen Sati so pending database updates can run. If the save still fails, give support the reference below instead of re-entering the client repeatedly.",
                supportReference);
        }

        if (exception is InvalidOperationException &&
            exception.Message.Contains("signed-in user", StringComparison.OrdinalIgnoreCase))
        {
            return Build(
                creating,
                saveStatusUnknown: false,
                problem: "Your signed-in Sati session is no longer available, so no save was attempted.",
                bestFix: "Sign in again, reopen Clients, and retry the save.",
                supportReference);
        }

        if (stage == ClientSaveStage.PreparingRecord)
        {
            return Build(
                creating,
                saveStatusUnknown: false,
                problem: "Sati could not finish preparing the client and compliance forms, so no save was attempted.",
                bestFix: "Review the dates and required fields, then retry. If it happens again, give support the reference below.",
                supportReference);
        }

        return Build(
            creating,
            saveStatusUnknown: true,
            problem: "Sati encountered an unexpected error while waiting for the save result.",
            bestFix: creating
                ? "Refresh the client list before retrying. If the client appears, do not add it again; otherwise give support the reference below."
                : "Close and reopen the Clients page and check the saved details before retrying. If your changes are present, do not save them again; otherwise give support the reference below.",
            supportReference);
    }

    private static ClientSaveProblem Build(
        bool creating,
        bool saveStatusUnknown,
        string problem,
        string bestFix,
        string? supportReference = null)
    {
        var saved = saveStatusUnknown
            ? creating
                ? "Sati cannot safely tell whether the new client was saved."
                : "Sati cannot safely tell whether the client changes were saved."
            : creating
                ? "No client record, compliance forms, lifecycle history, or audit entry was saved."
                : "The client changes were not saved.";
        var reference = string.IsNullOrWhiteSpace(supportReference)
            ? string.Empty
            : $"\n\nSupport reference: {supportReference}";

        return new ClientSaveProblem(
            saveStatusUnknown ? "Client Save Status Unconfirmed" : creating ? "Client Not Saved" : "Client Changes Not Saved",
            $"WHAT WAS SAVED\n{saved}\n\nWHAT WENT WRONG\n{problem}\n\nBEST FIX\n{bestFix}{reference}",
            saveStatusUnknown);
    }
}

public sealed class ClientSaveProblemEventArgs(ClientSaveProblem problem) : EventArgs
{
    public ClientSaveProblem Problem { get; } = problem;
}
