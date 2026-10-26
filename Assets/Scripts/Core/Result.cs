using System;

namespace SocraticTutor.Core
{
    /// <summary>
    /// Encapsulates the outcome of an operation that can fail predictably (network timeouts,
    /// inference errors, invalid payloads). Prevents raw exceptions from bubbling up to the VR presentation layer.
    /// </summary>
    public sealed class Result<T>
    {
        public bool IsSuccess { get; }
        public T Value { get; }
        public string ErrorMessage { get; }

        private Result(bool isSuccess, T value, string errorMessage)
        {
            IsSuccess = isSuccess;
            Value = value;
            ErrorMessage = errorMessage;
        }

        public static Result<T> Ok(T value) => new Result<T>(true, value, null);

        public static Result<T> Error(string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                throw new ArgumentException("An error result requires an explicit message.", nameof(errorMessage));
            }

            return new Result<T>(false, default, errorMessage);
        }
    }
}
