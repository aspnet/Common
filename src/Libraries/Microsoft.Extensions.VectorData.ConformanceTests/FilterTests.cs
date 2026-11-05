// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Linq.Expressions;
using Microsoft.Extensions.VectorData;
using VectorData.ConformanceTests.Support;
using Xunit;

#pragma warning disable CS8605 // Unboxing a possibly null value.
#pragma warning disable CS0252 // Possible unintended reference comparison; left hand side needs cast
#pragma warning disable RCS1098 // Constant values should be placed on right side of comparisons
#pragma warning disable CA1716 // Identifiers should not match keywords

namespace VectorData.ConformanceTests;

public abstract class FilterTests<TKey>(FilterTests<TKey>.Fixture fixture)
    where TKey : notnull
{
    #region Equality

    [Fact]
    public virtual Task Equal_with_int()
        => TestFilterAsync(
            r => r.Int == 8,
            r => (int)r["Int"] == 8);

    [Fact]
    public virtual Task Equal_with_string()
        => TestFilterAsync(
            r => r.String == "foo",
            r => r["String"] == "foo");

    [Fact]
    public virtual Task Equal_with_string_sql_injection_in_value()
    {
        string sqlInjection = $"foo; DROP TABLE {fixture.Collection.Name};";

        return TestFilterAsync(
            r => r.String == sqlInjection,
            r => r["String"] == sqlInjection,
            expectZeroResults: true);
    }

    [Fact]
    public virtual async Task Equal_with_string_sql_injection_in_name()
    {
        if (fixture.TestDynamic)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await fixture.DynamicCollection.SearchAsync(
                    new ReadOnlyMemory<float>([1, 2, 3]),
                    top: 1,
                    new() { Filter = r => r["String = \"not\"; DROP TABLE FilterTests;"] == "" }).ToListAsync());
        }
    }

    [Fact]
    public virtual Task Equal_with_string_containing_special_characters()
        => TestFilterAsync(
            r => r.String == fixture.SpecialCharactersText,
            r => r["String"] == fixture.SpecialCharactersText);

    [Fact]
    public virtual Task Equal_with_string_is_not_Contains()
        => TestFilterAsync(
            r => r.String == "some",
            r => r["String"] == "some",
            expectZeroResults: true);

#pragma warning disable SA1131 // Use readable conditions
    [Fact]
    public virtual Task Equal_reversed()
        => TestFilterAsync(
            r => 8 == r.Int,
            r => 8 == (int)r["Int"]);
#pragma warning restore SA1131

    [Fact]
    public virtual Task Equal_with_null_reference_type()
        => TestFilterAsync(
            r => r.String == null,
            r => r["String"] == null);

    [Fact]
    public virtual Task Equal_with_null_captured()
    {
        string? s = null;

        return TestFilterAsync(
            r => r.String == s,
            r => r["String"] == s);
    }

    [Fact]
    public virtual Task Equal_int_property_with_nonnull_nullable_int()
    {
        int? i = 8;

        return TestFilterAsync(
            r => r.Int == i,
            r => (int)r["Int"] == i);
    }

    [Fact]
    public virtual Task Equal_int_property_with_null_nullable_int()
    {
        int? i = null;

        return TestFilterAsync(
            r => r.Int == i,
            r => (int)r["Int"] == i,
            expectZeroResults: true);
    }

    [Fact]
    public virtual Task Equal_int_property_with_nonnull_nullable_int_Value()
    {
        int? i = 8;

        return TestFilterAsync(
            r => r.Int == i.Value,
            r => (int)r["Int"] == i.Value);
    }

#pragma warning disable CS8629 // Nullable value type may be null.
    [Fact]
    public virtual async Task Equal_int_property_with_null_nullable_int_Value()
    {
        int? i = null;

        // TODO: Some providers wrap filter translation exceptions in a VectorStoreException (#11766)
        var exception = await Assert.ThrowsAnyAsync<Exception>(() => TestFilterAsync(
            r => r.Int == i.Value,
            r => (int)r["Int"] == i.Value,
            expectZeroResults: true));

        if (exception is not InvalidOperationException and not VectorStoreException { InnerException: InvalidOperationException })
        {
            Assert.Fail($"Expected {nameof(InvalidOperationException)} or {nameof(VectorStoreException)} but got {exception.GetType()}");
        }
    }
#pragma warning restore CS8629

    [Fact]
    public virtual Task NotEqual_with_int()
        => TestFilterAsync(
            r => r.Int != 8,
            r => (int)r["Int"] != 8);

    [Fact]
    public virtual Task NotEqual_with_string()
        => TestFilterAsync(
            r => r.String != "foo",
            r => r["String"] != "foo");

    [Fact]
    public virtual Task NotEqual_reversed()
        => TestFilterAsync(
            r => r.Int != 8,
            r => (int)r["Int"] != 8);

    [Fact]
    public virtual Task NotEqual_with_null_reference_type()
        => TestFilterAsync(
            r => r.String != null,
            r => r["String"] != null);

    [Fact]
    public virtual Task NotEqual_with_null_captured()
    {
        string? s = null;

        return TestFilterAsync(
            r => r.String != s,
            r => r["String"] != s);
    }

    [Fact]
    public virtual Task Bool()
        => TestFilterAsync(
            r => r.Bool,
            r => (bool)r["Bool"]);

    [Fact]
    public virtual Task Bool_And_Bool()
        => TestFilterAsync(
            r => r.Bool && r.Bool,
            r => (bool)r["Bool"] && (bool)r["Bool"]);

    [Fact]
    public virtual Task Bool_Or_Not_Bool()
        => TestFilterAsync(
            r => r.Bool || !r.Bool,
            r => (bool)r["Bool"] || !(bool)r["Bool"],
            expectAllResults: true);

    #endregion Equality

    #region Comparison

    [Fact]
    public virtual Task GreaterThan_with_int()
        => TestFilterAsync(
            r => r.Int > 9,
            r => (int)r["Int"] > 9);

    [Fact]
    public virtual Task GreaterThanOrEqual_with_int()
        => TestFilterAsync(
            r => r.Int >= 9,
            r => (int)r["Int"] >= 9);

    [Fact]
    public virtual Task LessThan_with_int()
        => TestFilterAsync(
            r => r.Int < 10,
            r => (int)r["Int"] < 10);

    [Fact]
    public virtual Task LessThanOrEqual_with_int()
        => TestFilterAsync(
            r => r.Int <= 10,
            r => (int)r["Int"] <= 10);

    #endregion Comparison

    #region Logical operators

    [Fact]
    public virtual Task And()
        => TestFilterAsync(
            r => r.Int == 8 && r.String == "foo",
            r => (int)r["Int"] == 8 && r["String"] == "foo");

    [Fact]
    public virtual Task Or()
        => TestFilterAsync(
            r => r.Int == 8 || r.String == "foo",
            r => (int)r["Int"] == 8 || r["String"] == "foo");

    [Fact]
    public virtual Task And_within_And()
        => TestFilterAsync(
            r => (r.Int == 8 && r.String == "foo") && r.Int2 == 80,
            r => ((int)r["Int"] == 8 && r["String"] == "foo") && (int)r["Int2"] == 80);

    [Fact]
    public virtual Task And_within_Or()
        => TestFilterAsync(
            r => (r.Int == 8 && r.String == "foo") || r.Int2 == 100,
            r => ((int)r["Int"] == 8 && r["String"] == "foo") || (int)r["Int2"] == 100);

    [Fact]
    public virtual Task Or_within_And()
        => TestFilterAsync(
            r => (r.Int == 8 || r.Int == 9) && r.String == "foo",
            r => ((int)r["Int"] == 8 || (int)r["Int"] == 9) && r["String"] == "foo");

#pragma warning disable S1940 // Boolean checks should not be inverted
    [Fact]
    public virtual Task Not_over_Equal()
        => TestFilterAsync(
            r => !(r.Int == 8),
            r => !((int)r["Int"] == 8));

    [Fact]
    public virtual Task Not_over_NotEqual()
        => TestFilterAsync(
            r => !(r.Int != 8),
            r => !((int)r["Int"] != 8));
#pragma warning restore S1940 // Boolean checks should not be inverted

    [Fact]
    public virtual Task Not_over_And()
        => TestFilterAsync(
            r => !(r.Int == 8 && r.String == "foo"),
            r => !((int)r["Int"] == 8 && r["String"] == "foo"));

    [Fact]
    public virtual Task Not_over_Or()
        => TestFilterAsync(
            r => !(r.Int == 8 || r.String == "foo"),
            r => !((int)r["Int"] == 8 || r["String"] == "foo"));

    [Fact]
    public virtual Task Not_over_bool()
        => TestFilterAsync(
            r => !r.Bool,
            r => !(bool)r["Bool"]);

    [Fact]
    public virtual Task Not_over_bool_And_Comparison()
        => TestFilterAsync(
            r => !r.Bool && r.Int != int.MaxValue,
            r => !(bool)r["Bool"] && (int)r["Int"] != int.MaxValue);

    #endregion Logical operators

    #region Contains

    [Fact]
    public virtual Task Contains_over_field_string_array()
        => TestFilterAsync(
            r => r.StringArray.Contains("x"),
            r => ((string[])r["StringArray"]!).Contains("x"));

    [Fact]
    public virtual Task Contains_over_field_string_List()
        => TestFilterAsync(
            r => r.StringList.Contains("x"),
            r => ((List<string>)r["StringList"]!).Contains("x"));

    [Fact]
    public virtual Task Contains_over_inline_int_array()
        => TestFilterAsync(
            r => new[] { 8, 10 }.Contains(r.Int),
            r => new[] { 8, 10 }.Contains((int)r["Int"]));

    [Fact]
    public virtual Task Contains_over_inline_string_array()
        => TestFilterAsync(
            r => new[] { "foo", "baz", "unknown" }.Contains(r.String),
            r => new[] { "foo", "baz", "unknown" }.Contains(r["String"]));

    [Fact]
    public virtual Task Contains_over_inline_string_array_with_weird_chars()
        => TestFilterAsync(
            r => new[] { "foo", "baz", "un  , ' \"" }.Contains(r.String),
            r => new[] { "foo", "baz", "un  , ' \"" }.Contains(r["String"]));

    [Fact]
    public virtual Task Contains_over_captured_string_array()
    {
        var array = new[] { "foo", "baz", "unknown" };

        return TestFilterAsync(
            r => array.Contains(r.String),
            r => array.Contains(r["String"]));
    }

#pragma warning disable RCS1196 // Call extension method as instance method

    // C# 14 made changes to overload resolution to prefer Span-based overloads when those exist ("first-class spans");
    // this makes MemoryExtensions.Contains() be resolved rather than Enumerable.Contains() (see above).
    // See https://github.com/dotnet/runtime/issues/109757 for more context.
    // The following tests the various Contains variants directly, without using extension syntax, to ensure everything's
    // properly supported.
    [Fact]
    public virtual Task Contains_with_Enumerable_Contains()
        => TestFilterAsync(
            r => Enumerable.Contains(r.StringArray, "x"),
            r => ((string[])r["StringArray"]!).Contains("x"));

#if !NETFRAMEWORK
    [Fact]
    public virtual Task Contains_with_MemoryExtensions_Contains()
        => TestFilterAsync(
            r => MemoryExtensions.Contains(r.StringArray, "x"),
            r => ((string[])r["StringArray"]!).Contains("x"));
#endif

#if NET10_0_OR_GREATER
    [Fact]
    public virtual Task Contains_with_MemoryExtensions_Contains_with_null_comparer()
        => TestFilterAsync(
            r => MemoryExtensions.Contains(r.StringArray, "x", comparer: null),
            r => ((string[])r["StringArray"]!).Contains("x"));
#endif

#pragma warning restore RCS1196 // Call extension method as instance method

    [Fact]
    public virtual Task Any_with_Contains_over_inline_string_array()
        => TestFilterAsync(
            r => r.StringArray.Any(s => new[] { "x", "z", "nonexistent" }.Contains(s)),
            r => ((string[])r["StringArray"]!).Any(s => new[] { "x", "z", "nonexistent" }.Contains(s)));

    [Fact]
    public virtual Task Any_with_Contains_over_captured_string_array()
    {
        string[] tagsToFind = ["x", "z", "nonexistent"];

        return TestFilterAsync(
            r => r.StringArray.Any(s => tagsToFind.Contains(s)),
            r => ((string[])r["StringArray"]!).Any(s => tagsToFind.Contains(s)));
    }

    [Fact]
    public virtual Task Any_with_Contains_over_captured_string_list()
    {
        List<string> tagsToFind = ["x", "z", "nonexistent"];

        return TestFilterAsync(
            r => r.StringArray.Any(s => tagsToFind.Contains(s)),
            r => ((string[])r["StringArray"]!).Any(s => tagsToFind.Contains(s)));
    }

    [Fact]
    public virtual Task Any_over_List_with_Contains_over_captured_string_array()
    {
        string[] tagsToFind = ["x", "z", "nonexistent"];

        return TestFilterAsync(
            r => r.StringList.Any(s => tagsToFind.Contains(s)),
            r => ((List<string>)r["StringList"]!).Any(s => tagsToFind.Contains(s)));
    }

    #endregion Contains

    #region Variable types

    [Fact]
    public virtual Task Captured_local_variable()
    {
        // ReSharper disable once ConvertToConstant.Local
        var i = 8;

        return TestFilterAsync(
            r => r.Int == i,
            r => (int)r["Int"] == i);
    }

    [Fact]
    public virtual Task Member_field()
        => TestFilterAsync(
            r => r.Int == _memberField,
            r => (int)r["Int"] == _memberField);

    [Fact]
    public virtual Task Member_readonly_field()
        => TestFilterAsync(
            r => r.Int == _memberReadOnlyField,
            r => (int)r["Int"] == _memberReadOnlyField);

    [Fact]
    public virtual Task Member_static_field()
        => TestFilterAsync(
            r => r.Int == _staticMemberField,
            r => (int)r["Int"] == _staticMemberField);

    [Fact]
    public virtual Task Member_static_readonly_field()
        => TestFilterAsync(
            r => r.Int == _staticMemberReadOnlyField,
            r => (int)r["Int"] == _staticMemberReadOnlyField);

    [Fact]
    public virtual Task Member_nested_access()
        => TestFilterAsync(
            r => r.Int == _someWrapper.SomeWrappedValue,
            r => (int)r["Int"] == _someWrapper.SomeWrappedValue);

#pragma warning disable RCS1169 // Make field read-only
#pragma warning disable IDE0044 // Make field read-only
#pragma warning disable RCS1187 // Use constant instead of field
#pragma warning disable CA1802 // Use literals where appropriate
    private static readonly int _staticMemberReadOnlyField = 8;
    private static int _staticMemberField = 8;

    private readonly int _memberReadOnlyField = 8;
    private int _memberField = 8;

    private SomeWrapper _someWrapper = new();
#pragma warning restore CA1802
#pragma warning restore RCS1187
#pragma warning restore RCS1169
#pragma warning restore IDE0044

    private sealed class SomeWrapper
    {
        public int SomeWrappedValue = 8;
    }

    #endregion Variable types

    #region Miscellaneous

    [Fact]
    public virtual Task True()
        => TestFilterAsync(r => true, r => true, expectAllResults: true);

    #endregion Miscellaneous

    protected virtual async Task TestFilterAsync(
        Expression<Func<FilterRecord, bool>> filter,
        Expression<Func<Dictionary<string, object?>, bool>> dynamicFilter,
        bool expectZeroResults = false,
        bool expectAllResults = false)
    {
        var expected = fixture.TestData.AsQueryable().Where(filter).OrderBy(r => r.Key).ToList();

        if (expected.Count == 0 && !expectZeroResults)
        {
            Assert.Fail("The test returns zero results, and so may be unreliable");
        }
        else if (expectZeroResults && expected.Count != 0)
        {
            Assert.Fail($"{nameof(expectZeroResults)} was true, but the test returned {expected.Count} results.");
        }

        if (expected.Count == fixture.TestData.Count && !expectAllResults)
        {
            Assert.Fail("The test returns all results, and so may be unreliable");
        }
        else if (expectAllResults && expected.Count != fixture.TestData.Count)
        {
            Assert.Fail($"{nameof(expectAllResults)} was true, but the test returned {expected.Count} results instead of the expected {fixture.TestData.Count}.");
        }

        // Execute the query against the vector store, once using the strongly typed filter
        // and once using the dynamic filter
        var actual = await fixture.Collection.SearchAsync(
                new ReadOnlyMemory<float>([1, 2, 3]),
                top: fixture.TestData.Count,
                new() { Filter = filter })
            .Select(r => r.Record)
            .OrderBy(r => r.Key)
            .ToListAsync();

        if (actual.Count != expected.Count)
        {
            Assert.Fail($"Expected {expected.Count} results, but got {actual.Count}");
        }

        foreach (var (e, a) in expected.Zip(actual, (e, a) => (e, a)))
        {
            fixture.AssertEqualFilterRecord(e, a);
        }

        if (fixture.TestDynamic)
        {
            var dynamicActual = await fixture.DynamicCollection.SearchAsync(
                    new ReadOnlyMemory<float>([1, 2, 3]),
                    top: fixture.TestData.Count,
                    new() { Filter = dynamicFilter })
                .Select(r => r.Record)
                .OrderBy(r => r[nameof(FilterRecord.Key)])
                .ToListAsync();

            if (dynamicActual.Count != expected.Count)
            {
                Assert.Fail($"Expected {expected.Count} results, but got {actual.Count}");
            }

            foreach (var (e, a) in expected.Zip(dynamicActual, (e, a) => (e, a)))
            {
                fixture.AssertEqualDynamic(e, a);
            }
        }
    }

    public class FilterRecord : TestRecord<TKey>
    {
        public ReadOnlyMemory<float>? Vector { get; set; }

        public int Int { get; set; }
        public string? String { get; set; }
        public bool Bool { get; set; }
        public int Int2 { get; set; }
        public string[] StringArray { get; set; } = null!;
        public List<string> StringList { get; set; } = null!;
    }

    public abstract class Fixture : VectorStoreCollectionFixture<TKey, FilterRecord>
    {
        protected override string CollectionNameBase => nameof(FilterTests<int>);

        public virtual string SpecialCharactersText => """>with $om[ specia]"chara<ters'and\stuff""";

        public virtual VectorStoreCollection<object, Dictionary<string, object?>> DynamicCollection { get; protected set; } = null!;

        public virtual bool TestDynamic => true;

        public override async ValueTask InitializeAsync()
        {
            await base.InitializeAsync();

            if (TestDynamic)
            {
                DynamicCollection = TestStore.CreateDynamicCollection(CollectionName, CreateRecordDefinition());
            }
        }

        public override VectorStoreCollectionDefinition CreateRecordDefinition()
            => new()
            {
                Properties =
                [
                    new VectorStoreKeyProperty(nameof(FilterRecord.Key), typeof(TKey)),
                    new VectorStoreVectorProperty(nameof(FilterRecord.Vector), typeof(ReadOnlyMemory<float>?), 3)
                    {
                        DistanceFunction = DistanceFunction,
                        IndexKind = IndexKind
                    },

                    new VectorStoreDataProperty(nameof(FilterRecord.Int), typeof(int)) { IsIndexed = true },
                    new VectorStoreDataProperty(nameof(FilterRecord.String), typeof(string)) { IsIndexed = true },
                    new VectorStoreDataProperty(nameof(FilterRecord.Bool), typeof(bool)) { IsIndexed = true },
                    new VectorStoreDataProperty(nameof(FilterRecord.Int2), typeof(int)) { IsIndexed = true },
                    new VectorStoreDataProperty(nameof(FilterRecord.StringArray), typeof(string[])) { IsIndexed = true },
                    new VectorStoreDataProperty(nameof(FilterRecord.StringList), typeof(List<string>)) { IsIndexed = true }
                ]
            };

        protected override List<FilterRecord> BuildTestData()
        {
            // All records have the same vector - this fixture is about testing criteria filtering only
            var vector = new ReadOnlyMemory<float>([1, 2, 3]);

            return
            [
                new()
                {
                    Key = GenerateNextKey<TKey>(),
                    Int = 8,
                    String = "foo",
                    Bool = true,
                    Int2 = 80,
                    StringArray = ["x", "y"],
                    StringList = ["x", "y"],
                    Vector = vector
                },
                new()
                {
                    Key = GenerateNextKey<TKey>(),
                    Int = 9,
                    String = "bar",
                    Bool = false,
                    Int2 = 90,
                    StringArray = ["a", "b"],
                    StringList = ["a", "b"],
                    Vector = vector
                },
                new()
                {
                    Key = GenerateNextKey<TKey>(),
                    Int = 9,
                    String = "foo",
                    Bool = true,
                    Int2 = 9,
                    StringArray = ["x"],
                    StringList = ["x"],
                    Vector = vector
                },
                new()
                {
                    Key = GenerateNextKey<TKey>(),
                    Int = 10,
                    String = null,
                    Bool = false,
                    Int2 = 100,
                    StringArray = ["x", "y", "z"],
                    StringList = ["x", "y", "z"],
                    Vector = vector
                },
                new()
                {
                    Key = GenerateNextKey<TKey>(),
                    Int = 11,
                    Bool = true,
                    String = SpecialCharactersText,
                    Int2 = 101,
                    StringArray = ["y", "z"],
                    StringList = ["y", "z"],
                    Vector = vector
                }
            ];
        }

        public virtual void AssertEqualFilterRecord(FilterRecord x, FilterRecord y)
        {
            var definitionProperties = CreateRecordDefinition().Properties;

            Assert.Equal(x.Key, y.Key);
            Assert.Equal(x.Int, y.Int);
            Assert.Equal(x.String, y.String);
            Assert.Equal(x.Int2, y.Int2);

            if (definitionProperties.Any(p => p.Name == nameof(FilterRecord.Bool)))
            {
                Assert.Equal(x.Bool, y.Bool);
            }

            if (definitionProperties.Any(p => p.Name == nameof(FilterRecord.StringArray)))
            {
                Assert.Equivalent(x.StringArray, y.StringArray);
            }

            if (definitionProperties.Any(p => p.Name == nameof(FilterRecord.StringList)))
            {
                Assert.Equivalent(x.StringList, y.StringList);
            }
        }

        public virtual void AssertEqualDynamic(FilterRecord x, Dictionary<string, object?> y)
        {
            var definitionProperties = CreateRecordDefinition().Properties;

            Assert.Equal(x.Key, y["Key"]);
            Assert.Equal(x.Int, y["Int"]);
            Assert.Equal(x.String, y["String"]);
            Assert.Equal(x.Int2, y["Int2"]);

            if (definitionProperties.Any(p => p.Name == nameof(FilterRecord.Bool)))
            {
                Assert.Equal(x.Bool, y["Bool"]);
            }

            if (definitionProperties.Any(p => p.Name == nameof(FilterRecord.StringArray)))
            {
                Assert.Equivalent(x.StringArray, y["StringArray"]);
            }

            if (definitionProperties.Any(p => p.Name == nameof(FilterRecord.StringList)))
            {
                Assert.Equivalent(x.StringList, y["StringList"]);
            }
        }

        // In some databases (Azure AI Search), the data shows up but the filtering index isn't yet updated,
        // so filtered searches show empty results. Add a filter to the seed data check below.
        protected override Task WaitForDataAsync()
            => TestStore.WaitForDataAsync(Collection, recordCount: TestData.Count, r => r.Int > 0);
    }
}
