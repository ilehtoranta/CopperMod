using System;
using System.IO;
using System.Collections.Generic;

namespace CopperMod.Abstractions
{
    /// <summary>
    /// Provides module bytes plus optional source-file context for loaders that need sibling data files.
    /// </summary>
    public sealed class ModuleLoadContext
    {
        private readonly IReadOnlyDictionary<string, string>? _relativeFileAliases;
        private readonly Func<string, byte[]?>? _relativeFileResolver;
        /// <summary>
        /// Creates a load context from module bytes and an optional source path.
        /// </summary>
        public ModuleLoadContext(ReadOnlyMemory<byte> data, string? sourcePath = null)
            : this(data, sourcePath, null)
        {
        }

        /// <summary>Creates a context with filename aliases for companion files, resolved within the source directory.</summary>
        public ModuleLoadContext(ReadOnlyMemory<byte> data, string? sourcePath, IReadOnlyDictionary<string, string>? relativeFileAliases)
            : this(data, sourcePath, relativeFileAliases, null)
        {
        }

        /// <summary>Creates a context with a custom companion-file resolver.</summary>
        public ModuleLoadContext(ReadOnlyMemory<byte> data, string? sourcePath, IReadOnlyDictionary<string, string>? relativeFileAliases, Func<string, byte[]?>? relativeFileResolver)
        {
            _relativeFileResolver = relativeFileResolver;
            Data = data;
            SourcePath = string.IsNullOrWhiteSpace(sourcePath) ? null : Path.GetFullPath(sourcePath);
            _relativeFileAliases = relativeFileAliases;
        }

        /// <summary>
        /// Module bytes to detect and load.
        /// </summary>
        public ReadOnlyMemory<byte> Data { get; }

        /// <summary>
        /// Span view of <see cref="Data" /> for existing byte-oriented parsers.
        /// </summary>
        public ReadOnlySpan<byte> DataSpan => Data.Span;

        /// <summary>
        /// Full path of the source module file, when the module was loaded from a file.
        /// </summary>
        public string? SourcePath { get; }

        /// <summary>
        /// Directory containing the source module file, when available.
        /// </summary>
        public string? SourceDirectory => SourcePath == null ? null : Path.GetDirectoryName(SourcePath);

        /// <summary>Adds companion aliases while preserving the file resolver.</summary>
        public ModuleLoadContext WithRelativeFileAliases(IReadOnlyDictionary<string, string> aliases)
            => new ModuleLoadContext(Data, SourcePath, aliases, _relativeFileResolver);

        /// <summary>
        /// Reads a file below <see cref="SourceDirectory" /> without allowing traversal outside that directory.
        /// </summary>
        public bool TryReadRelativeFile(string relativePath, out byte[] data)
        {
            data = Array.Empty<byte>();
            if (string.IsNullOrWhiteSpace(relativePath) || SourceDirectory == null)
            {
                return false;
            }

            if (_relativeFileAliases != null && _relativeFileAliases.TryGetValue(relativePath, out var alias))
            {
                relativePath = alias;
            }

            if (_relativeFileResolver != null)
            {
                var resolved = _relativeFileResolver(relativePath);
                if (resolved == null) return false;
                data = resolved;
                return true;
            }

            var fullDirectory = Path.GetFullPath(SourceDirectory);
            var fullPath = Path.GetFullPath(Path.Combine(fullDirectory, relativePath));
            var directoryWithSeparator = fullDirectory.EndsWith(Path.DirectorySeparatorChar)
                ? fullDirectory
                : fullDirectory + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(directoryWithSeparator, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(fullPath, fullDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!File.Exists(fullPath))
            {
                return false;
            }

            data = File.ReadAllBytes(fullPath);
            return true;
        }
    }
}
