using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using ExpenseTracker.Api.Services;

namespace ExpenseTracker.Api.Controllers;

// [ApiController] answers input it can't bind or validate with ProblemDetails. This keeps that shape (`errors` still
// lists every field) but rewrites the serializer's messages into plain words and adds `error`: the first problem,
// like every other 400 from this API.
public static partial class InvalidInput
{
    public static IActionResult Response(ActionContext context)
    {
        // A body that fails to parse also reports its parameter ("dto") as missing; that adds nothing.
        var bodyParameters = context.ActionDescriptor.Parameters
            .Where(p => p.BindingInfo?.BindingSource == BindingSource.Body)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var entries = context.ModelState
            .Where(e => e.Value is { Errors.Count: > 0 })
            .Select(e => (Key: e.Key, Messages: bodyParameters.Contains(e.Key)
                ? new[] { "The request body is missing." }
                : e.Value!.Errors.Select(x => Friendly(e.Key, x)).Distinct().ToArray()))
            .ToList();
        if (entries.Count > 1)
            entries.RemoveAll(e => bodyParameters.Contains(e.Key));

        // Serializer problems first: they explain why other fields look missing.
        var ordered = entries.OrderBy(e => e.Key.StartsWith('$') || e.Key.Length == 0 ? 0 : 1).ToList();
        var errors = ordered.ToDictionary(e => e.Key.Length == 0 ? "$" : e.Key, e => e.Messages);

        var details = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred."
        };
        details.Extensions["error"] = ordered.FirstOrDefault().Messages?.FirstOrDefault() ?? "The request isn't valid.";
        details.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier;
        return new BadRequestObjectResult(details) { ContentTypes = { "application/problem+json" } };
    }

    private static string Friendly(string key, ModelError error)
    {
        var message = string.IsNullOrEmpty(error.ErrorMessage) ? error.Exception?.Message ?? string.Empty : error.ErrorMessage;
        var field = FieldName(key);

        if (message.StartsWith("A non-empty request body is required", StringComparison.Ordinal))
            return "The request body is missing.";
        // An upload over the request size limit fails while the form is read, before the action runs.
        if (message.StartsWith("Failed to read the request form", StringComparison.Ordinal))
            return message.Contains("too large", StringComparison.OrdinalIgnoreCase) ? "That upload is too large." : "The upload couldn't be read. Try again.";
        if (message.StartsWith(YearMonthJsonConverter.Invalid, StringComparison.Ordinal))
            return $"{Or(field, "Month")} must be a month like 2026-11.";

        var conversion = ConversionPattern().Match(message);
        if (conversion.Success)
        {
            if (field.Length == 0) return "The request body has the wrong shape.";
            var type = conversion.Groups["type"].Value;
            if (type.Contains("DateOnly") || type.Contains("DateTime")) return $"{field} must be a date like 2026-11-05.";
            if (type.Contains("YearMonth")) return $"{field} must be a month like 2026-11.";
            if (type.Contains("Decimal") || type.Contains("Double")) return $"{field} must be a number.";
            if (type.Contains("Int")) return $"{field} must be a whole number.";
            if (type.Contains("Boolean")) return $"{field} must be true or false.";
            if (type.Contains("Guid")) return $"{field} isn't a valid id.";
            if (type.Contains("String")) return $"{field} must be text.";
            return $"{field} has the wrong shape.";
        }

        // Anything else the JSON reader reports (bad syntax, trailing commas, a missing brace…).
        if (message.Contains("LineNumber:", StringComparison.Ordinal) || message.Contains("JSON", StringComparison.Ordinal))
            return "The request isn't valid JSON.";
        return message;
    }

    // "$.allocations[0].receivedDate" → "Received date"; "Description" → "Description".
    private static string FieldName(string key)
    {
        var last = key.Split('.').Last();
        last = IndexPattern().Replace(last, string.Empty).TrimStart('$');
        if (last.Length == 0) return string.Empty;
        var words = WordPattern().Replace(last, " $1").Trim().ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string Or(string value, string fallback) => value.Length == 0 ? fallback : value;

    [GeneratedRegex(@"could not be converted to (?<type>[^\s]+?)\.?(\s|$)")]
    private static partial Regex ConversionPattern();

    [GeneratedRegex(@"\[\d+\]")]
    private static partial Regex IndexPattern();

    [GeneratedRegex(@"(?<=[a-z0-9])([A-Z])")]
    private static partial Regex WordPattern();
}
