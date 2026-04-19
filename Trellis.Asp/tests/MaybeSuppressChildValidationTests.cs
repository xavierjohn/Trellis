namespace Trellis.Asp.Tests;

using System;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using Trellis;
using Trellis.Asp.Validation;
using Xunit;

/// <summary>
/// Tests for <see cref="MaybeSuppressChildValidationMetadataProvider"/>.
/// Verifies that MVC's ValidationVisitor does not recurse into <see cref="Maybe{T}"/> properties.
/// </summary>
public class MaybeSuppressChildValidationTests
{
    #region Test Value Objects

    public class Name : ScalarValueObject<Name, string>, IScalarValue<Name, string>
    {
        private Name(string value) : base(value) { }

        public static Result<Name> TryCreate(string? value, string? fieldName = null)
        {
            var field = fieldName ?? "name";
            if (string.IsNullOrWhiteSpace(value))
                return Result.Fail<Asp.Tests.MaybeSuppressChildValidationTests.Name>(new Error.UnprocessableContent(EquatableArray.Create(new FieldViolation(InputPointer.ForProperty(field), "validation.error") { Detail = "Name is required." })));
            return Result.Ok(new Name(value));
        }
    }

    #endregion

    #region ValidateChildren = false for Maybe<T>

    [Fact]
    public void CreateValidationMetadata_MaybeType_SetsValidateChildrenFalse()
    {
        // Arrange
        var provider = new MaybeSuppressChildValidationMetadataProvider();
        var key = ModelMetadataIdentity.ForType(typeof(Maybe<Name>));
        var context = new ValidationMetadataProviderContext(key, GetModelAttributes());

        // Act
        provider.CreateValidationMetadata(context);

        // Assert
        context.ValidationMetadata.ValidateChildren.Should().BeFalse(
            "MVC should not recurse into Maybe<T>.Value to avoid InvalidOperationException when HasNoValue");
    }

    [Fact]
    public void CreateValidationMetadata_MaybeString_SetsValidateChildrenFalse()
    {
        // Arrange — even Maybe<string> should be suppressed
        var provider = new MaybeSuppressChildValidationMetadataProvider();
        var key = ModelMetadataIdentity.ForType(typeof(Maybe<string>));
        var context = new ValidationMetadataProviderContext(key, GetModelAttributes());

        // Act
        provider.CreateValidationMetadata(context);

        // Assert
        context.ValidationMetadata.ValidateChildren.Should().BeFalse();
    }

    #endregion

    #region Non-Maybe types not affected

    [Fact]
    public void CreateValidationMetadata_DirectValueObject_DoesNotSetValidateChildren()
    {
        // Arrange
        var provider = new MaybeSuppressChildValidationMetadataProvider();
        var key = ModelMetadataIdentity.ForType(typeof(Name));
        var context = new ValidationMetadataProviderContext(key, GetModelAttributes());

        // Act
        provider.CreateValidationMetadata(context);

        // Assert
        context.ValidationMetadata.ValidateChildren.Should().BeNull(
            "non-Maybe types should not have ValidateChildren modified");
    }

    [Fact]
    public void CreateValidationMetadata_PlainString_DoesNotSetValidateChildren()
    {
        // Arrange
        var provider = new MaybeSuppressChildValidationMetadataProvider();
        var key = ModelMetadataIdentity.ForType(typeof(string));
        var context = new ValidationMetadataProviderContext(key, GetModelAttributes());

        // Act
        provider.CreateValidationMetadata(context);

        // Assert
        context.ValidationMetadata.ValidateChildren.Should().BeNull();
    }

    [Fact]
    public void CreateValidationMetadata_PlainInt_DoesNotSetValidateChildren()
    {
        // Arrange
        var provider = new MaybeSuppressChildValidationMetadataProvider();
        var key = ModelMetadataIdentity.ForType(typeof(int));
        var context = new ValidationMetadataProviderContext(key, GetModelAttributes());

        // Act
        provider.CreateValidationMetadata(context);

        // Assert
        context.ValidationMetadata.ValidateChildren.Should().BeNull();
    }

    [Fact]
    public void CreateValidationMetadata_GenericListNotMaybe_DoesNotSetValidateChildren()
    {
        // Arrange — List<string> is generic but not Maybe<>
        var provider = new MaybeSuppressChildValidationMetadataProvider();
        var key = ModelMetadataIdentity.ForType(typeof(System.Collections.Generic.List<string>));
        var context = new ValidationMetadataProviderContext(key, GetModelAttributes());

        // Act
        provider.CreateValidationMetadata(context);

        // Assert
        context.ValidationMetadata.ValidateChildren.Should().BeNull();
    }

    #endregion

    #region Helpers

    private static ModelAttributes GetModelAttributes() =>
        ModelAttributes.GetAttributesForType(typeof(object));

    #endregion
}
