﻿using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using PayloadLogging.Common.Extensions;
using PayloadLogging.Common.Models.PayloadLogging;
using PayloadLogging.Common.Models.PayloadLogging.Enum;
using PayloadLogging.Common.RestClients.Interface;
using PayloadLogging.Common.Settings;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PayloadLogging.Common.Middlewares
{
  public class PayloadLoggingMiddleware
  {
    private readonly RequestDelegate _next;
    private readonly ILogger _logger;
    private readonly IRestService<PayloadModel> _restService;
    private readonly RecyclableMemoryStreamManager _recyclableMemoryStreamManager;
    private readonly List<string> _ignoreUrls;

    private const string MethodName = "payload";

    public PayloadLoggingMiddleware(RequestDelegate next, ILoggerFactory loggerFactory, IRestService<PayloadModel> restService)
    {
      _next = next ?? throw new ArgumentNullException(nameof(next));
      _restService = restService ?? throw new ArgumentNullException(nameof(restService));
      _logger = loggerFactory.CreateLogger<PayloadLoggingMiddleware>();
      _recyclableMemoryStreamManager = new RecyclableMemoryStreamManager();
      _ignoreUrls = ApiSettings.IgnorePayloadUrls.Split(",", StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    public async Task Invoke(HttpContext context)
    {
      var correlationId = await LogRequest(context).ConfigureAwait(false);
      await LogResponse(context, correlationId).ConfigureAwait(false);
    }

    private async Task<string> LogRequest(HttpContext context)
    {
      context.Request.EnableBuffering();
      await using var requestStream = _recyclableMemoryStreamManager.GetStream();
      await context.Request.Body.CopyToAsync(requestStream).ConfigureAwait(false);
      var requestBody = ReadStreamInChunks(requestStream);
      context.Request.Body.Position = 0;

      if (IsUrlIgnored(context.Request.Path))
      {
        return null;
      }

      var payload = BuildPayloadModel(context, requestBody, PayloadType.Request);

      var result = await _restService.Post(ApiSettings.PayloadLoggingHost, MethodName, payload).ConfigureAwait(false);
      if (!result.IsSuccessful)
      {
        _logger.LogWarning($"Failed to write request to Payload Api with CorrelationId: '{ payload.CorrelationId }', Error: {result.Content}");
      }

      _logger.LogInformation($"{ PayloadType.Request } payload has been logged with CorrelationId: '{ payload.CorrelationId ?? string.Empty }'");
      return payload.CorrelationId;
    }

    private async Task LogResponse(HttpContext context, string correlationId)
    {
      var originalBodyStream = context.Response.Body;
      await using var responseBody = _recyclableMemoryStreamManager.GetStream();
      context.Response.Body = responseBody;
      await _next(context).ConfigureAwait(false);
      context.Response.Body.Seek(0, SeekOrigin.Begin);
      var requestBody = await new StreamReader(context.Response.Body).ReadToEndAsync().ConfigureAwait(false);
      context.Response.Body.Seek(0, SeekOrigin.Begin);
      await responseBody.CopyToAsync(originalBodyStream).ConfigureAwait(false);

      if (IsUrlIgnored(context.Request.Path))
      {
        return;
      }

      var payload = BuildPayloadModel(context, requestBody, PayloadType.Response, correlationId);

      var result = await _restService.Post(ApiSettings.PayloadLoggingHost, MethodName, payload).ConfigureAwait(false);
      if (!result.IsSuccessful)
      {
        _logger.LogWarning($"Failed to write request to Payload Api with CorrelationId: '{ payload.CorrelationId }', Error: {result.Content}");
      }

      _logger.LogInformation($"{ PayloadType.Response } payload has been logged with CorrelationId: '{ payload.CorrelationId ?? string.Empty }'");
    }

    #region Private methods

    private bool IsUrlIgnored(PathString requestPath)
    {
      return _ignoreUrls.Any(ignoreUrl => requestPath.ToUriComponent().Contains(ignoreUrl) || requestPath.Equals("/"));
    }

    private PayloadModel BuildPayloadModel(HttpContext context, string requestBody, PayloadType payloadType, string correlationId = null)
    {
      var queryStringValue = context.Request.QueryString.Value;
      var headers = context.Request.Headers?.ToStringDictionary();

      var payload = new PayloadModel
      {
        Source = $"{context.Request.Host}{context.Request.Path}{queryStringValue}",
        HttpVerb = context.Request.Method,
        Headers = headers.ToStringValue(),
        Type = payloadType.ToString(),
        Payload = requestBody.MinifyJsonText(),
        Query = queryStringValue
      };

      switch (payloadType)
      {
        case PayloadType.Request:
          payload.CorrelationId = headers != null &&
                                  headers.TryGetValue("X-PB-CorrelationId",
                                    out var payloadCorrelationId) && !string.IsNullOrWhiteSpace(payloadCorrelationId)
            ? payloadCorrelationId
            : $"{DateTime.UtcNow:yyyyMMddHHmmssffff}-{payload.Payload.Length}";
          break;

        case PayloadType.Response:
          payload.CorrelationId = correlationId;
          payload.ResponseCode = context.Response.StatusCode;
          break;

        default:
          throw new InvalidOperationException("Invalid Payload type");
      }

      var stringBuilder = new StringBuilder()
        .AppendLine("Http Response Information:")
        .Append("Payload Type: ").AppendLine(payload.Type)
        .Append("Request Type: ").AppendLine(payload.HttpVerb)
        .Append("Source: ").AppendLine(payload.Source)
        .Append("Headers: ").AppendLine(payload.Headers)
        .Append("StatusCode: ").Append(payload.ResponseCode).AppendLine()
        .Append("Response Body: ").AppendLine(payload.Payload);

      _logger.LogDebug(stringBuilder.ToString());

      return payload;
    }

    private static string ReadStreamInChunks(Stream stream)
    {
      const int readChunkBufferLength = 4096;

      stream.Seek(0, SeekOrigin.Begin);

      using var textWriter = new StringWriter();
      using var reader = new StreamReader(stream);

      var readChunk = new char[readChunkBufferLength];
      int readChunkLength;

      do
      {
        readChunkLength = reader.ReadBlock(readChunk, 0, readChunkBufferLength);
        textWriter.Write(readChunk, 0, readChunkLength);
      } while (readChunkLength > 0);

      return textWriter.ToString();
    }

    #endregion Private methods
  }
}