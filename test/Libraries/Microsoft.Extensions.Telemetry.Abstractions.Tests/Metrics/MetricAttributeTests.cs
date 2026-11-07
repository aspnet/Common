// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace Microsoft.Extensions.Diagnostics.Metrics.Test;

public class MetricAttributeTests
{
    private const string MyMetric = "MyMetric";
    private const string MyDescription = "My metric description.";

    [Fact]
    public void TestCounterAttribute()
    {
        var attribute = new CounterAttribute("d1", "d2", "d3");
        Assert.NotNull(attribute);
        Assert.Null(attribute.Name);
        Assert.Null(attribute.Type);
        Assert.Null(attribute.Unit);
        Assert.Null(attribute.Description);
        Assert.Equal(new[] { "d1", "d2", "d3" }, attribute.TagNames);

        attribute.Name = MyMetric;
        Assert.Equal(MyMetric, attribute.Name);
        attribute.Unit = "items";
        Assert.Equal("items", attribute.Unit);
        attribute.Description = MyDescription;
        Assert.Equal(MyDescription, attribute.Description);
    }

    [Fact]
    public void TestHistogramAttribute()
    {
        var attribute = new HistogramAttribute("d1", "d2", "d3");
        Assert.NotNull(attribute);
        Assert.Null(attribute.Name);
        Assert.Null(attribute.Type);
        Assert.Null(attribute.Unit);
        Assert.Null(attribute.Description);
        Assert.Equal(new[] { "d1", "d2", "d3" }, attribute.TagNames);

        attribute.Name = MyMetric;
        Assert.Equal(MyMetric, attribute.Name);
        attribute.Unit = "items";
        Assert.Equal("items", attribute.Unit);
        attribute.Description = MyDescription;
        Assert.Equal(MyDescription, attribute.Description);
    }

    [Fact]
    public void TestCounterAttributeT()
    {
        var attribute = new CounterAttribute<int>("d1", "d2", "d3");
        Assert.NotNull(attribute);
        Assert.Null(attribute.Name);
        Assert.Null(attribute.Type);
        Assert.Null(attribute.Unit);
        Assert.Null(attribute.Description);
        Assert.Equal(new[] { "d1", "d2", "d3" }, attribute.TagNames);

        attribute.Name = MyMetric;
        Assert.Equal(MyMetric, attribute.Name);
        attribute.Unit = "items";
        Assert.Equal("items", attribute.Unit);
        attribute.Description = MyDescription;
        Assert.Equal(MyDescription, attribute.Description);
    }

    [Fact]
    public void TestHistogramAttributeT()
    {
        var attribute = new HistogramAttribute<int>("d1", "d2", "d3");
        Assert.NotNull(attribute);
        Assert.Null(attribute.Name);
        Assert.Null(attribute.Type);
        Assert.Null(attribute.Unit);
        Assert.Null(attribute.Description);
        Assert.Equal(new[] { "d1", "d2", "d3" }, attribute.TagNames);

        attribute.Name = MyMetric;
        Assert.Equal(MyMetric, attribute.Name);
        attribute.Unit = "items";
        Assert.Equal("items", attribute.Unit);
        attribute.Description = MyDescription;
        Assert.Equal(MyDescription, attribute.Description);
    }

    [Fact]
    public void TestGaugeAttribute()
    {
        var attribute = new GaugeAttribute("d1", "d2", "d3");
        Assert.NotNull(attribute);
        Assert.Null(attribute.Name);
        Assert.Null(attribute.Type);
        Assert.Null(attribute.Unit);
        Assert.Null(attribute.Description);
        Assert.Equal(new[] { "d1", "d2", "d3" }, attribute.TagNames);

        attribute.Name = MyMetric;
        Assert.Equal(MyMetric, attribute.Name);
        attribute.Unit = "items";
        Assert.Equal("items", attribute.Unit);
        attribute.Description = MyDescription;
        Assert.Equal(MyDescription, attribute.Description);
    }

    [Fact]
    public void TestGaugeAttributeT()
    {
        var attribute = new GaugeAttribute<int>("d1", "d2", "d3");
        Assert.NotNull(attribute);
        Assert.Null(attribute.Name);
        Assert.Null(attribute.Type);
        Assert.Null(attribute.Unit);
        Assert.Null(attribute.Description);
        Assert.Equal(new[] { "d1", "d2", "d3" }, attribute.TagNames);

        attribute.Name = MyMetric;
        Assert.Equal(MyMetric, attribute.Name);
        attribute.Unit = "items";
        Assert.Equal("items", attribute.Unit);
        attribute.Description = MyDescription;
        Assert.Equal(MyDescription, attribute.Description);
    }

    [Fact]
    public void TestStrongTypeCounterAttribute()
    {
        var attribute = new CounterAttribute(typeof(TagNameTest));

        Assert.NotNull(attribute);
        Assert.Null(attribute.Name);
        Assert.Null(attribute.TagNames);
        Assert.Null(attribute.Unit);
        Assert.Null(attribute.Description);
        Assert.Equal(typeof(TagNameTest), attribute.Type);

        attribute.Name = MyMetric;
        Assert.Equal(MyMetric, attribute.Name);
        attribute.Unit = "items";
        Assert.Equal("items", attribute.Unit);
        attribute.Description = MyDescription;
        Assert.Equal(MyDescription, attribute.Description);
    }

    [Fact]
    public void TestStrongTypeHistogramAttribute()
    {
        var attribute = new HistogramAttribute(typeof(TagNameTest));

        Assert.NotNull(attribute);
        Assert.Null(attribute.Name);
        Assert.Null(attribute.TagNames);
        Assert.Null(attribute.Unit);
        Assert.Null(attribute.Description);
        Assert.Equal(typeof(TagNameTest), attribute.Type);

        attribute.Name = MyMetric;
        Assert.Equal(MyMetric, attribute.Name);
        attribute.Unit = "items";
        Assert.Equal("items", attribute.Unit);
        attribute.Description = MyDescription;
        Assert.Equal(MyDescription, attribute.Description);
    }

    [Fact]
    public void TestStrongTypeCounterAttributeT()
    {
        var attribute = new CounterAttribute<byte>(typeof(TagNameTest));

        Assert.NotNull(attribute);
        Assert.Null(attribute.Name);
        Assert.Null(attribute.TagNames);
        Assert.Null(attribute.Unit);
        Assert.Null(attribute.Description);
        Assert.Equal(typeof(TagNameTest), attribute.Type);

        attribute.Name = MyMetric;
        Assert.Equal(MyMetric, attribute.Name);
        attribute.Unit = "items";
        Assert.Equal("items", attribute.Unit);
        attribute.Description = MyDescription;
        Assert.Equal(MyDescription, attribute.Description);
    }

    [Fact]
    public void TestStrongTypeHistogramAttributeT()
    {
        var attribute = new HistogramAttribute<byte>(typeof(TagNameTest));

        Assert.NotNull(attribute);
        Assert.Null(attribute.Name);
        Assert.Null(attribute.TagNames);
        Assert.Null(attribute.Unit);
        Assert.Null(attribute.Description);
        Assert.Equal(typeof(TagNameTest), attribute.Type);

        attribute.Name = MyMetric;
        Assert.Equal(MyMetric, attribute.Name);
        attribute.Unit = "items";
        Assert.Equal("items", attribute.Unit);
        attribute.Description = MyDescription;
        Assert.Equal(MyDescription, attribute.Description);
    }

    [Fact]
    public void TestStrongTypeGaugeAttribute()
    {
        var attribute = new GaugeAttribute(typeof(TagNameTest));

        Assert.NotNull(attribute);
        Assert.Null(attribute.Name);
        Assert.Null(attribute.TagNames);
        Assert.Null(attribute.Unit);
        Assert.Null(attribute.Description);
        Assert.Equal(typeof(TagNameTest), attribute.Type);

        attribute.Name = MyMetric;
        Assert.Equal(MyMetric, attribute.Name);
        attribute.Unit = "items";
        Assert.Equal("items", attribute.Unit);
        attribute.Description = MyDescription;
        Assert.Equal(MyDescription, attribute.Description);
    }

    [Fact]
    public void TestStrongTypeGaugeAttributeT()
    {
        var attribute = new GaugeAttribute<byte>(typeof(TagNameTest));

        Assert.NotNull(attribute);
        Assert.Null(attribute.Name);
        Assert.Null(attribute.TagNames);
        Assert.Null(attribute.Unit);
        Assert.Null(attribute.Description);
        Assert.Equal(typeof(TagNameTest), attribute.Type);

        attribute.Name = MyMetric;
        Assert.Equal(MyMetric, attribute.Name);
        attribute.Unit = "items";
        Assert.Equal("items", attribute.Unit);
        attribute.Description = MyDescription;
        Assert.Equal(MyDescription, attribute.Description);
    }

    [Fact]
    public void TestTagNameAttribute()
    {
        var attribute = new TagNameAttribute("testName");

        Assert.NotNull(attribute);
        Assert.Equal("testName", attribute.Name);
    }

    public class TagNameTest
    {
        public string? D1 { get; set; }
        public string? D2 { get; set; }
        public string? D3 { get; set; }
    }
}