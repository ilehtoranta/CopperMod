using System;

namespace CopperMod.Abstractions
{
    /// <summary>
    /// Severity for a module parse or render diagnostic.
    /// </summary>
    public enum ModuleDiagnosticSeverity
    {
        /// <summary>
        /// Informational note.
        /// </summary>
        Info,

        /// <summary>
        /// Non-fatal compatibility or data warning.
        /// </summary>
        Warning,

        /// <summary>
        /// Error that affected loading or rendering.
        /// </summary>
        Error
    }

    /// <summary>
    /// Describes a non-fatal module loading or playback issue.
    /// </summary>
    public sealed class ModuleDiagnostic
    {
        /// <summary>
        /// Creates a diagnostic.
        /// </summary>
        public ModuleDiagnostic(ModuleDiagnosticSeverity severity, string message, string? code = null)
        {
            Severity = severity;
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Code = code;
        }

        /// <summary>
        /// Diagnostic severity.
        /// </summary>
        public ModuleDiagnosticSeverity Severity { get; }

        /// <summary>
        /// Human-readable diagnostic text.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Optional stable diagnostic code.
        /// </summary>
        public string? Code { get; }

        /// <inheritdoc />
        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Code)
                ? $"{Severity}: {Message}"
                : $"{Severity} {Code}: {Message}";
        }
    }
}
