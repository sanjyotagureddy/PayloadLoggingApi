﻿using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace PayloadLogging.Common.Models.PayloadLogging
{
  public class PayloadModel
  {
    /// <summary>
    /// Full source url of the request/response
    /// </summary>
    /// <value>Name of client application submitting the payload</value>
    public string Source { get; set; }

    /// <summary>
    /// HttpVerb generated by the client application
    /// </summary>
    [Required]
    [DataMember(Name = "httpVerb")]
    public string HttpVerb { get; set; }

    /// <summary>
    /// When available QueryString generated by the client application
    /// </summary>
    [DataMember(Name = "query")]
    public string Query { get; set; }

    /// <summary>
    /// When available Headers received or generated by the client application
    /// </summary>
    [DataMember(Name = "headers")]
    public string Headers { get; set; }

    /// <summary>
    /// Full payload received or generated by the client application
    /// </summary>
    /// <value>Full payload received or generated by the client application</value>
    public string Payload { get; set; }

    /// <summary>
    /// Type of Payload
    /// </summary>
    public string Type { get; set; }

    /// <summary>
    /// Correlation Id
    /// </summary>
    public string CorrelationId { get; set; }

    /// <summary>
    /// When available response code generated by the client application
    /// </summary>
    /// <value>When available response code generated by the client application</value>
    public int? ResponseCode { get; set; }
  }
}