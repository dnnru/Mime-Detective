﻿using MimeDetective.Engine;
using MimeDetective.Storage;
using System.Collections.Immutable;
using System.Linq;

namespace MimeDetective {


    internal class ContentInspectorImpl
        : ContentInspector
    {
        public ContentInspectorImpl(
            ImmutableArray<Definition> Definitions, 
            DefinitionMatchEvaluatorOptions MatchEvaluatorOptions,
            StringSegmentMatcherProvider StringSegmentIndex,
            bool Parallel
            ) {

            this.StringSegmentIndex = StringSegmentIndex;
            this.MatchEvaluatorOptions = MatchEvaluatorOptions;
            this.Parallel = Parallel;
            this.DefinitionMatchers = GenerateDefinitionMatchers(StringSegmentIndex, Definitions);
        }

        protected StringSegmentMatcherProvider StringSegmentIndex { get; }

        protected DefinitionMatchEvaluatorOptions MatchEvaluatorOptions { get; }
        protected ImmutableArray<DefinitionMatcher> DefinitionMatchers { get; }
        protected bool Parallel { get; }

        private static ImmutableArray<DefinitionMatcher> GenerateDefinitionMatchers(StringSegmentMatcherProvider StringSegmentIndex, ImmutableArray<Definition> Definitions) {

            var Prefixes = (from x in Definitions from y in x.Signature.Prefix select y).Distinct(PrefixSegmentEqualityComparer.Instance).ToList();
            var Strings = (from x in Definitions from y in x.Signature.Strings select y).Distinct(StringSegmentEqualityComparer.Instance).ToList();

            var PrefixMatcherCache = Prefixes
                .ToImmutableDictionary(
                x => x,
                x => PrefixSegmentMatcher.Create(x),
                PrefixSegmentEqualityComparer.Instance
                );

            var StringMatcherCache = Strings
                .ToImmutableDictionary(
                x => x,
                x => StringSegmentIndex.CreateMatcher(x),
                StringSegmentEqualityComparer.Instance
                );

            var ret = (
                from Definition in Definitions

                let PrefixMatchers = (
                    from y in Definition.Signature.Prefix
                    select PrefixMatcherCache[y]
                ).ToImmutableArray()

                let StringMatchers = (
                    from y in Definition.Signature.Strings
                    select StringMatcherCache[y]
                    ).ToImmutableArray()

                select new DefinitionMatcher(Definition) {
                    Prefixes = PrefixMatchers,
                    Strings = StringMatchers,
                }).ToImmutableArray();


            return ret;
        }


        public ImmutableArray<DefinitionMatch> Inspect(ImmutableArray<byte> Content)
        {
            var ret = Inspect_Current(Content);

            return ret;
        }
        
        protected ImmutableArray<DefinitionMatch> Inspect_Current(ImmutableArray<byte> Content)
        {

            var NoContentStrings = ImmutableArray<StringSegment>.Empty;


            var MatchEvaluator1 = new DefinitionMatchEvaluator()
            {
                Options = MatchEvaluatorOptions with
                {
                    Include_Segments_Strings = false
                }
            };


            var tret = (
                from DefinitionMatcher in DefinitionMatchers.AsParallel(Parallel)

                let Match = MatchEvaluator1.Match(
                    DefinitionMatcher, 
                    Content, 
                    NoContentStrings
                    )
                where Match is { }
                orderby Match.Points descending
                select new {
                    Match,
                    DefinitionMatcher
                }
                ).ToImmutableArray();


            if (MatchEvaluatorOptions.Include_Segments_Strings) {

                var Source2 = tret;

                var NeedsStrings = (
                    from x in Source2
                    where x.DefinitionMatcher.Strings.Length > 0
                    select x
                    ).Any()
                    ;
                if(NeedsStrings)
                {
                    var MatchEvaluator2 = new DefinitionMatchEvaluator()
                    {
                        Options = MatchEvaluatorOptions
                    };

                    var ContentStrings = StringSegmentExtrator.ExtractStrings(Content);


                    tret = (
                        from x in Source2.AsParallel(Parallel)
                        let Match = MatchEvaluator2.Match(
                            x.DefinitionMatcher,
                            Content,
                            ContentStrings
                            )
                        where Match is { }
                        orderby Match.Points descending
                        select new {
                            Match,
                            x.DefinitionMatcher,
                        }
                        ).ToImmutableArray();

                } else
                {

                }

            }

            var ret = (
                from x in tret
                select x.Match
                ).ToImmutableArray();

            return ret;
        }

    }
}
