//  Copyright (C) 2013, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Linq;
using System.Collections.Generic;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// Describes the different complexities of file lists
    ///</summary>
    public enum FilterType : int
    {
        /// <summary>
        /// No filter expression
        /// </summary>
        Empty,
        /// <summary>
        /// A simple list of names
        /// </summary>
        Simple,
        /// <summary>
        /// A list of files described with wildcards
        /// </summary>
        Wildcard,
        /// <summary>
        /// A list of files described with regular expressions
        /// </summary>
        Regexp
    }

    /// <summary>
    /// Represents a filter that can comprise multiple filter strings
    /// </summary>    
    public class FilterExpression : IFilter
    {
        private struct FilterEntry
        {
            public readonly FilterType Type;
            public readonly string Filter;
            public readonly System.Text.RegularExpressions.Regex Regexp;
            
            /// <summary>
            /// The single wildcard character (DOS style)
            /// </summary>
            private const char SINGLE_WILDCARD = '?';
            /// <summary>
            /// The multiple wildcard character (DOS style)
            /// </summary>
            private const char MULTIPLE_WILDCARD = '*';
            
            
            private static readonly System.Text.RegularExpressions.RegexOptions REGEXP_OPTIONS = Library.Utility.Utility.IsFSCaseSensitive ? System.Text.RegularExpressions.RegexOptions.Compiled : System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase;
            
            public FilterEntry(string filter)
            {
                if (string.IsNullOrEmpty(filter))
                {
                    this.Type = FilterType.Empty;
                    this.Filter = null;
                    this.Regexp = null;
                }
                if (filter.StartsWith("[") && filter.EndsWith("]"))
                {
                    this.Type = FilterType.Regexp;
                    this.Filter = filter.Substring(1, filter.Length - 2);
                    this.Regexp = new System.Text.RegularExpressions.Regex(this.Filter, REGEXP_OPTIONS);
                }
                else
                {
                    this.Type = (filter.Contains(MULTIPLE_WILDCARD) || filter.Contains(SINGLE_WILDCARD)) ? FilterType.Wildcard : FilterType.Simple;
                    this.Filter = (!Utility.IsFSCaseSensitive && this.Type == FilterType.Wildcard) ? filter.ToUpper() : filter;
                    this.Regexp = new System.Text.RegularExpressions.Regex(Library.Utility.Utility.ConvertGlobbingToRegExp(filter), REGEXP_OPTIONS);
                }
            }
            
            /// <summary>
            /// Tests whether specified string can be matched agains provided pattern string. Pattern may contain single- and multiple-replacing
            /// wildcard characters.
            /// </summary>
            /// <param name="input">String which is matched against the pattern.</param>
            /// <param name="pattern">Pattern against which string is matched.</param>
            /// <returns>true if <paramref name="pattern"/> matches the string <paramref name="input"/>; otherwise false.</returns>
            /// <note>From: http://www.c-sharpcorner.com/uploadfile/b81385/efficient-string-matching-algorithm-with-use-of-wildcard-characters/</note>
            private static bool IsWildcardMatch(string input, string pattern)
            {
                int[] inputPosStack = new int[(input.Length + 1) * (pattern.Length + 1)];   // Stack containing input positions that should be tested for further matching
                int[] patternPosStack = new int[inputPosStack.Length];                      // Stack containing pattern positions that should be tested for further matching
                int stackPos = -1;                                                          // Points to last occupied entry in stack; -1 indicates that stack is empty
                bool[,] pointTested = new bool[input.Length + 1, pattern.Length + 1];       // Each true value indicates that input position vs. pattern position has been tested
                int inputPos = 0;   // Position in input matched up to the first multiple wildcard in pattern
                int patternPos = 0; // Position in pattern matched up to the first multiple wildcard in pattern
                // Match beginning of the string until first multiple wildcard in pattern
                while (inputPos < input.Length && patternPos < pattern.Length && pattern[patternPos] != MULTIPLE_WILDCARD && (input[inputPos] == pattern[patternPos] || pattern[patternPos] == SINGLE_WILDCARD))
                {
                    inputPos++;
                    patternPos++;
                }
                
                
                // Push this position to stack if it points to end of pattern or to a general wildcard
                if (patternPos == pattern.Length || pattern[patternPos] == MULTIPLE_WILDCARD)
                {
                    pointTested[inputPos, patternPos] = true;
                    inputPosStack[++stackPos] = inputPos;
                    patternPosStack[stackPos] = patternPos;
                }
                bool matched = false;
                // Repeat matching until either string is matched against the pattern or no more parts remain on stack to test
                while (stackPos >= 0 && !matched)
                {
                    inputPos = inputPosStack[stackPos];         // Pop input and pattern positions from stack
                    patternPos = patternPosStack[stackPos--];   // Matching will succeed if rest of the input string matches rest of the pattern
                    
                    // Modified from original version to match zero or more characters
                    //if (inputPos == input.Length && patternPos == pattern.Length)
                    
                    if (inputPos == input.Length && (patternPos == pattern.Length || (patternPos == pattern.Length - 1 && pattern[patternPos] == MULTIPLE_WILDCARD)))
                        matched = true;     // Reached end of both pattern and input string, hence matching is successful
                    else
                    {   
                        // First character in next pattern block is guaranteed to be multiple wildcard
                        // So skip it and search for all matches in value string until next multiple wildcard character is reached in pattern
                        for(int curInputStart = inputPos; curInputStart < input.Length; curInputStart++)
                        {
                            int curInputPos = curInputStart;
                            int curPatternPos = patternPos + 1;
                            if (curPatternPos == pattern.Length)
                            {   // Pattern ends with multiple wildcard, hence rest of the input string is matched with that character
                                curInputPos = input.Length;
                            }
                            else
                            {
                                while (curInputPos < input.Length && curPatternPos < pattern.Length && pattern[curPatternPos] != MULTIPLE_WILDCARD &&
                                    (input[curInputPos] == pattern[curPatternPos] || pattern[curPatternPos] == SINGLE_WILDCARD))
                                {
                                    curInputPos++;
                                    curPatternPos++;
                                }
                            }
                            // If we have reached next multiple wildcard character in pattern without breaking the matching sequence, then we have another candidate for full match
                            // This candidate should be pushed to stack for further processing
                            // At the same time, pair (input position, pattern position) will be marked as tested, so that it will not be pushed to stack later again
                            if (((curPatternPos == pattern.Length && curInputPos == input.Length) || (curPatternPos < pattern.Length && pattern[curPatternPos] == MULTIPLE_WILDCARD)) 
                                && !pointTested[curInputPos, curPatternPos])
                            {
                                pointTested[curInputPos, curPatternPos] = true;
                                inputPosStack[++stackPos] = curInputPos;
                                patternPosStack[stackPos] = curPatternPos;
                            }
                        }
                    }
                }
                return matched;
            }
            
            /// <summary>
            /// Gets a value indicating if the filter matches the path
            /// </summary>
            /// <param name="path">The path to match</param>
            public bool Matches(string path)
            {
                switch (this.Type)
                {
                    case FilterType.Simple:
                        return string.Equals(this.Filter, path, Library.Utility.Utility.ClientFilenameStringComparision);
                    case FilterType.Wildcard:
                        return IsWildcardMatch(!Utility.IsFSCaseSensitive ? path.ToUpper() : path, this.Filter);
                    case FilterType.Regexp:
                        var m = this.Regexp.Match(path);
                        return m.Success && m.Length == path.Length;
                    default:
                        return false;                            
                }
            }
        }
    
        /// <summary>
        /// The internal list of expressions
        /// </summary>
        private List<FilterEntry> m_filters;
    
        /// <summary>
        /// Gets the type of the filter
        /// </summary>
        public readonly FilterType Type;

        /// <summary>
        /// Gets the result returned if an entry matches
        /// </summary>        
        public readonly bool Result;
        
        /// <summary>
        /// Gets a value indicating whether this <see cref="Duplicati.Library.Utility.FilterExpression"/> is empty.
        /// </summary>
        /// <value><c>true</c> if empty; otherwise, <c>false</c>.</value>
        public bool Empty { get { return this.Type == FilterType.Empty; } }
        
        /// <summary>
        /// Gets the simple list, if the type is simple, named or wildcard
        /// </summary>
        /// <returns>The simple list</returns>
        public string[] GetSimpleList()
        {
            if (this.Type == FilterType.Simple || this.Type == FilterType.Wildcard)
                return (from n in m_filters select n.Filter).ToArray();
            else
                throw new InvalidOperationException(string.Format("Cannot extract simple list when the type is: {0}", this.Type));
        }
        
        /// <summary>
        /// Gets a value indicating if the filter matches the path
        /// </summary>
        /// <param name="result">The match result</param>
        /// <param name="result">The match result</param>
        /// <param name="match">The filter that matched</param>
        public bool Matches(string path, out bool result, out IFilter match)
        {
            result = false;
            if (this.Type == FilterType.Empty)
            {
                match = null;
                return false;
            }
            
            if (m_filters.Where(x => x.Matches(path)).Any())
            {
                match = this;
                result = this.Result;
                return true;
            }
            
            match = null;
            return false;
        }

        /// <summary>
        /// Creates a new <see cref="Duplicati.Library.Utility.FilterExpression"/> instance, representing an empty filter.
        /// </summary>
        /// <param name="filter">The filter string that represents the filter</param>
        public FilterExpression()
            : this((IEnumerable<string>)null, true)
        {
        }
    
        /// <summary>
        /// Creates a new <see cref="Duplicati.Library.Utility.FilterExpression"/> instance.
        /// </summary>
        /// <param name="filter">The filter string that represents the filter</param>
        public FilterExpression(string filter, bool result = true)
            : this(new string[] { filter }, result)
        {
        }
    
        /// <summary>
        /// Creates a new <see cref="Duplicati.Library.Main.FilterExpression"/> instance.
        /// </summary>
        /// <param name="filter">The filter string that represents the filter</param>
        public FilterExpression(IEnumerable<string> filter, bool result = true)
        {
            this.Result = result;
            
            if (filter == null)
            {
                this.Type = FilterType.Empty;
                return;
            }
            
            m_filters = Compact(
                (from n in filter
                let nx = new FilterEntry(n)
                where nx.Type != FilterType.Empty
                select nx)
            );
            
            if (m_filters.Count == 0)
                this.Type = FilterType.Empty;
            else
                this.Type = (FilterType)m_filters.Max((a) => a.Type);
        }
        
        private static List<FilterEntry> Compact(IEnumerable<FilterEntry> items)
        {
            var r = new List<FilterEntry>();
            string combined = null;
            bool first = false;
            foreach(var f in items)
                if (combined == null)
                {
                    if (f.Type == FilterType.Simple || f.Type == FilterType.Wildcard)
                        r.Add(f);
                    else if (f.Type != FilterType.Empty)
                    {
                        combined = f.Regexp.ToString();
                        first = true;
                    }
                }
                else
                {
                    if (f.Type == FilterType.Simple || f.Type == FilterType.Wildcard)
                    {
                        r.Add(new FilterEntry("[" + combined + "]"));
                        r.Add(f);
                        combined = null;
                    }
                    else if (f.Type != FilterType.Empty)
                    {
                        if (first)
                        {
                            combined = "(" + combined + ")";
                            first = false;
                        }
                        
                        combined += "|(" + f.Regexp.ToString() + ")";
                    }
                }
                
            if (combined != null)
                r.Add(new FilterEntry("[" + combined + "]"));

            return r;                    
        }
        
        /// <summary>
        /// Utility function to match a filter with a default fall-through value
        /// </summary>
        /// <param name="filter">The filter to evaluate</param>
        /// <param name="path">The path to evaluate</param>
        public static bool Matches(IFilter filter, string path)
        {
            IFilter match;
            return Matches(filter, path, out match);
        }
        
        /// <summary>
        /// Utility function to match a filter with a default fall-through value
        /// </summary>
        /// <param name="filter">The filter to evaluate</param>
        /// <param name="path">The path to evaluate</param>
        /// <param name="match">The filter that matched</param>
        public static bool Matches(IFilter filter, string path, out IFilter match)
        {
            if (filter == null || filter.Empty)
            {
                match = null;
                return true;
            }
        
            bool result;
            if (filter.Matches(path, out result, out match))
                return result;

            // If we only exclude files, choose to include by default
            var q = new Queue<IFilter>();
            q.Enqueue(filter);
                
            while (q.Count > 0)
            {
                var p = q.Dequeue();
                if (p == null || p.Empty)
                    continue;
                else if (p is FilterExpression && ((FilterExpression)filter).Result)
                {
                    match = p;
                    return false; // We have an include filter, so we exclude by default
                }
                else if (p is JoinedFilterExpression)
                {
                    q.Enqueue(((JoinedFilterExpression)p).First);
                    q.Enqueue(((JoinedFilterExpression)p).Second);
                }
            }
                    
            // Only excludes, we return true
            match = null;
            return true;
        }
        
        /// <summary>
        /// Combine the specified filter expressions.
        /// </summary>
        /// <param name="first">First.</param>
        /// <param name="second">Second.</param>
        public static FilterExpression Combine(FilterExpression first, FilterExpression second)
        {
            return new FilterExpression(first.m_filters.Union(second.m_filters).Select(x => x.Type == FilterType.Regexp ? ("[" + x.Filter + "]") : x.Filter), first.Result);
        }
        
        public override string ToString()
        {
            if (this.Empty)
                return "";
            
            return 
                "(" +
                string.Join(") || (",
                    (from n in m_filters
                        select n.Type == FilterType.Regexp ? "[" + n.Filter + "]" : n.Filter)
                ) +
                ")";
        }
    }
}

