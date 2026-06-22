namespace FortressSouls.Api;

using FortressSouls.Application;
using Microsoft.AspNetCore.Http.HttpResults;

internal static class ChatEndpoints
{
    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder endpoints, bool includePromptPreview)
    {
        var group = endpoints.MapGroup("/api/chat");

        group.MapPost("/sessions", CreateSessionAsync)
            .WithName("CreateChatSession")
            .Produces<CreateChatSessionResponse>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status408RequestTimeout)
            .Produces<ApiErrorResponse>(StatusCodes.Status500InternalServerError);

        group.MapPost("/sessions/{sessionId}/messages", SendMessageAsync)
            .WithName("SendChatMessage")
            .Produces<SendChatMessageResponse>()
            .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<ApiErrorResponse>(StatusCodes.Status408RequestTimeout)
            .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<ApiErrorResponse>(StatusCodes.Status502BadGateway)
            .Produces<ApiErrorResponse>(StatusCodes.Status504GatewayTimeout)
            .Produces<ApiErrorResponse>(StatusCodes.Status500InternalServerError)
            .Produces<ApiErrorResponse>(StatusCodes.Status503ServiceUnavailable);

        if (includePromptPreview)
        {
            group.MapGet("/sessions/{sessionId}/prompt-preview", GetPromptPreview)
                .WithName("GetChatPromptPreview")
                .Produces<PromptPreviewResponse>()
                .Produces<ApiErrorResponse>(StatusCodes.Status400BadRequest)
                .Produces<ApiErrorResponse>(StatusCodes.Status404NotFound)
                .Produces<ApiErrorResponse>(StatusCodes.Status409Conflict);
        }

        return endpoints;
    }

    private static async Task<IResult> CreateSessionAsync(
        CreateChatSessionRequest request,
        ChatSessionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.CreateSessionAsync(request.DwarfId, cancellationToken);
            return TypedResults.Ok(new CreateChatSessionResponse(result.SessionId, result.DwarfId));
        }
        catch (OperationCanceledException)
        {
            return TypedResults.Json(
                new ApiErrorResponse("request_cancelled", "The request was cancelled."),
                statusCode: StatusCodes.Status408RequestTimeout);
        }
        catch (DwarfNotFoundException)
        {
            return TypedResults.NotFound(new ApiErrorResponse("dwarf_not_found", "The requested dwarf was not found."));
        }
        catch (ChatValidationException exception)
        {
            return TypedResults.BadRequest(new ApiErrorResponse(exception.ErrorCode, exception.Message));
        }
        catch (DwarfFortressDataException)
        {
            return TypedResults.Json(
                new ApiErrorResponse("dwarf_data_invalid", "The dwarf data source returned invalid data."),
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    private static async Task<IResult> SendMessageAsync(
        string sessionId,
        SendChatMessageRequest request,
        ChatSessionService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.SendMessageAsync(sessionId, request.Message, cancellationToken);
            return TypedResults.Ok(
                new SendChatMessageResponse(
                    SessionId: result.SessionId,
                    DwarfId: result.DwarfId,
                    AssistantMessage: new ChatAssistantMessageResponse("assistant", result.AssistantMessage),
                    Diagnostics: new ChatDiagnosticsResponse(
                        result.Diagnostics.Provider,
                        result.Diagnostics.Model,
                        result.Diagnostics.DurationMs,
                        result.Diagnostics.PromptId),
                    ToolReceipts: result.ToolReceipts
                        .Select(receipt => new ChatToolReceiptResponse(receipt.Tool, receipt.Outcome))
                        .ToArray()));
        }
        catch (OperationCanceledException)
        {
            return TypedResults.Json(
                new ApiErrorResponse("request_cancelled", "The request was cancelled."),
                statusCode: StatusCodes.Status408RequestTimeout);
        }
        catch (ChatSessionNotFoundException)
        {
            return TypedResults.NotFound(new ApiErrorResponse("chat_session_not_found", "The chat session was not found."));
        }
        catch (ChatTurnInProgressException)
        {
            return TypedResults.Conflict(new ApiErrorResponse("chat_turn_in_progress", "A chat turn is already in progress for this session."));
        }
        catch (ChatValidationException exception)
        {
            var statusCode = exception.ErrorCode == "prompt_preview_unavailable"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;

            return TypedResults.Json(new ApiErrorResponse(exception.ErrorCode, exception.Message), statusCode: statusCode);
        }
        catch (ChatProviderException exception)
        {
            var (statusCode, errorCode, message) = exception.ErrorCode switch
            {
                ChatProviderErrorCode.Timeout => (StatusCodes.Status504GatewayTimeout, "chat_provider_timeout", "The chat provider timed out."),
                ChatProviderErrorCode.Unavailable => (StatusCodes.Status503ServiceUnavailable, "chat_provider_unavailable", "The chat provider is unavailable."),
                ChatProviderErrorCode.ResponseTooLarge => (StatusCodes.Status502BadGateway, "chat_provider_invalid_response", "The chat provider returned an invalid response."),
                ChatProviderErrorCode.InvalidResponse => (StatusCodes.Status502BadGateway, "chat_provider_invalid_response", "The chat provider returned an invalid response."),
                ChatProviderErrorCode.InvalidConfiguration => (StatusCodes.Status500InternalServerError, "chat_provider_invalid_configuration", "The chat provider configuration is invalid."),
                _ => (StatusCodes.Status500InternalServerError, "chat_provider_error", "The chat provider failed to process the request.")
            };

            return TypedResults.Json(new ApiErrorResponse(errorCode, message), statusCode: statusCode);
        }
    }

    private static Results<Ok<PromptPreviewResponse>, NotFound<ApiErrorResponse>, JsonHttpResult<ApiErrorResponse>> GetPromptPreview(
        string sessionId,
        ChatSessionService service)
    {
        try
        {
            var result = service.GetPromptPreview(sessionId);
            return TypedResults.Ok(new PromptPreviewResponse(result.SessionId, result.DwarfId, result.PromptText));
        }
        catch (ChatSessionNotFoundException)
        {
            return TypedResults.NotFound(new ApiErrorResponse("chat_session_not_found", "The chat session was not found."));
        }
        catch (ChatValidationException exception)
        {
            var statusCode = exception.ErrorCode == "prompt_preview_unavailable"
                ? StatusCodes.Status409Conflict
                : StatusCodes.Status400BadRequest;

            return TypedResults.Json(new ApiErrorResponse(exception.ErrorCode, exception.Message), statusCode: statusCode);
        }
    }
}
