﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace MAB.DotIgnore
{
    /// <summary>
    /// A list of ignore rules
    /// </summary>
    public class IgnoreList
    {
        private List<IgnoreRule> _rules = new List<IgnoreRule>();

        /// <summary>
        /// The individual ignore rules currently loaded into this list
        /// </summary>
        public ReadOnlyCollection<IgnoreRule> Rules { get { return _rules.AsReadOnly(); } }

        /// <summary>
        /// Create a list of ignore rules to check paths against
        /// </summary>
        /// <param name="rules">A list of glob ignore patterns as strings</param>
        /// <param name="flags">Optional flags determining pattern matching behaviour</param>
        public IgnoreList(IEnumerable<string> rules, MatchFlags flags = MatchFlags.PATHNAME)
        {
            AddRules(rules, flags);
        }

        /// <summary>
        /// Create a list of ignore rules to check paths against
        /// </summary>
        /// <param name="ignoreFilePath">Path to a text file containing a list of glob ignore patterns as strings</param>
        /// <param name="flags">Optional flags determining pattern matching behaviour</param>
        public IgnoreList(string ignoreFilePath, MatchFlags flags = MatchFlags.PATHNAME)
        {
            AddRules(File.ReadAllLines(ignoreFilePath), flags);
        }

        /// <summary>
        /// Add a rule to the ignore list
        /// </summary>
        /// <param name="rule">Glob ignore pattern as string</param>
        /// <param name="flags">Optional flags determining pattern matching behaviour</param>
        public void AddRule(string rule, MatchFlags flags = MatchFlags.PATHNAME)
        {
            AddRules(new string[] { rule }, flags);
        }

        /// <summary>
        /// Add multiple rules to the ignore list
        /// </summary>
        /// <param name="ignoreFilePath">Path to a text file containing a list of glob ignore patterns as strings</param>
        /// <param name="flags">Optional flags determining pattern matching behaviour</param>
        public void AddRules(string ignoreFilePath, MatchFlags flags = MatchFlags.PATHNAME)
        {
            _rules.AddRange(CleanRules(File.ReadAllLines(ignoreFilePath)).Select(line => new IgnoreRule(line, flags)));
        }

        /// <summary>
        /// Add multiple rules to the ignore list
        /// </summary>
        /// <param name="rules">A list of glob ignore patterns as strings</param>
        /// <param name="flags">Optional flags determining pattern matching behaviour</param>
        public void AddRules(IEnumerable<string> rules, MatchFlags flags = MatchFlags.PATHNAME)
        {
            _rules.AddRange(CleanRules(rules).Select(line => new IgnoreRule(line, flags)));
        }

        /// <summary>
        /// Remove a rule from the ignore list
        /// </summary>
        /// <param name="rule">Glob ignore pattern as string</param>
        public void RemoveRule(string rule)
        {
            _rules.RemoveAll(r => r.OriginalPattern == rule.Trim());
        }
        
        /// <summary>
        /// Check if a file path matches any of the rules in the ignore list
        /// </summary>
        /// <param name="file">FileInfo representing the file to check</param>
        public bool IsIgnored(FileInfo file)
        {
            return IsIgnored(file.FullName, false);
        }

        /// <summary>
        /// Check if a directory path matches any of the rules in the ignore list
        /// </summary>
        /// <param name="file">FileInfo representing the file to check</param>
        /// <param name="log">List of strings to append log messages to</param>
        public bool IsIgnored(FileInfo file, IgnoreLog log)
        {
            return IsIgnored(file.FullName, false, log);
        }

        /// <summary>
        /// Check if a directory path matches any of the rules in the ignore list
        /// </summary>
        /// <param name="directory">DirectoryInfo representing the file to check</param>
        public bool IsIgnored(DirectoryInfo directory)
        {
            return IsIgnored(directory.FullName, true);
        }

        /// <summary>
        /// Check if a directory path matches any of the rules in the ignore list
        /// </summary>
        /// <param name="directory">DirectoryInfo representing the file to check</param>
        /// <param name="log">List of strings to append log messages to</param>
        public bool IsIgnored(DirectoryInfo directory, IgnoreLog log)
        {
            return IsIgnored(directory.FullName, true, log);
        }

        /// <summary>
        /// Check if a string path matches any of the rules in the ignore list
        /// </summary>
        /// <param name="path">String representing the path to check</param>
        /// <param name="pathIsDirectory">Should be set True if the path represents a directory, False if it represents a file</param>
        public bool IsIgnored(string path, bool pathIsDirectory)
        {
            return IsIgnored(path, pathIsDirectory, null);
        }

        /// <summary>
        /// Check if a string path matches any of the rules in the ignore list
        /// </summary>
        /// <param name="path">String representing the path to check</param>
        /// <param name="pathIsDirectory">Should be set True if the path represents a directory, False if it represents a file</param>
        /// <param name="log">List of strings to append log messages to</param>
        public bool IsIgnored(string path, bool pathIsDirectory, IgnoreLog log)
        {
            var pathIgnored = IsPathIgnored(path, pathIsDirectory, log);

            if(pathIsDirectory)
                return pathIgnored;

            var ancestorIgnored = IsAnyParentDirectoryIgnored(path, log);

            if(ancestorIgnored)
                return true;

            return pathIgnored;
        }

        /// <summary>
        /// Create an exact copy of the ignore list
        /// </summary>
        public IgnoreList Clone()
        {
            return new IgnoreList(_rules.Select(x => x.OriginalPattern));
        }

        private IEnumerable<string> CleanRules(IEnumerable<string> rules)
        {
            // Exclude all comment or whitespace lines
            return rules.Select(line => line.Trim())
                        .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.OrdinalIgnoreCase));
        }

        private bool IsPathIgnored(string path, bool pathIsDirectory, IgnoreLog log)
        {
            // This pattern modified from https://github.com/henon/GitSharp/blob/master/GitSharp/IgnoreRules.cs
            var ignore = false;

            foreach (var rule in _rules)
            {
                if (rule.IsMatch(path, pathIsDirectory))
                {
                    ignore = !rule.PatternFlags.HasFlag(PatternFlags.NEGATION);

                    if (log != null)
                    {
                        if(!log.ContainsKey(path))
                            log.Add(path, new List<string>());

                        var entry = string.Format(
                            "{0} by {1}", 
                            (rule.PatternFlags.HasFlag(PatternFlags.NEGATION) ? "INCLUDED" : "IGNORED"), 
                            rule.ToString()
                        );

                        log[path].Add(entry);
                    }
                }
            }

            return ignore;
        }

        private bool IsAnyParentDirectoryIgnored(string path, IgnoreLog log)
        {
            var segments = Utils.NormalisePath(path).Split('/').ToList();
            segments.RemoveAt(segments.Count - 1);

            var directory = new List<string>();

            // Loop over all the path segments (moving down the directory tree) 
            // and test each as a directory, returning immediately if true
            foreach (var segment in segments)
            {
                directory.Add(segment);
                if(IsPathIgnored(string.Join("/", directory.ToArray()), true, log))
                    return true;
            }

            return false;
        }
    }
}
