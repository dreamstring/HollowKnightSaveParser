using System;

namespace HollowKnightSaveParser.Models
{
    public class ConversionResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string OutputPath { get; set; }
        public Exception Exception { get; set; }

        public static ConversionResult CreateSuccess(string message, string outputPath = null)
        {
            return new ConversionResult
            {
                Success = true,
                Message = message,
                OutputPath = outputPath
            };
        }

        public static ConversionResult CreateFailure(string message, Exception exception = null)
        {
            return new ConversionResult
            {
                Success = false,
                Message = message,
                Exception = exception
            };
        }
    }
}