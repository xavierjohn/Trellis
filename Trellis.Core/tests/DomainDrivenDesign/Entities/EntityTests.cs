namespace Trellis.Core.Tests.DomainDrivenDesign.Entities;

using Trellis;

public class EntityTests
{
    #region GetHashCode Tests

    [Fact]
    public void Entity_GetHashCode_DoesNotCauseStackOverflow()
    {
        // Arrange - This test verifies the fix for HashCode.Combine(this, Id) -> HashCode.Combine(GetType(), Id)
        // The old implementation would cause a stack overflow because 'this' would recursively call GetHashCode
        var entity = TestEntity.Create("123");

        // Act - Should not throw StackOverflowException
        var hashCode = entity.GetHashCode();

        // Assert
        hashCode.Should().NotBe(0);
    }

    [Fact]
    public void Entity_GetHashCode_IsDeterministic()
    {
        // Arrange
        var entity = TestEntity.Create("123");

        // Act
        var hash1 = entity.GetHashCode();
        var hash2 = entity.GetHashCode();

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void Entity_GetHashCode_SameIdSameType_SameHashCode()
    {
        // Arrange
        var entity1 = TestEntity.Create("123");
        var entity2 = TestEntity.Create("123");

        // Act
        var hash1 = entity1.GetHashCode();
        var hash2 = entity2.GetHashCode();

        // Assert
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void Entity_GetHashCode_DifferentIds_DifferentHashCodes()
    {
        // Arrange
        var entity1 = TestEntity.Create("123");
        var entity2 = TestEntity.Create("456");

        // Act
        var hash1 = entity1.GetHashCode();
        var hash2 = entity2.GetHashCode();

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void Entity_GetHashCode_SameIdDifferentTypes_DifferentHashCodes()
    {
        // Arrange
        var entity1 = TestEntity.Create("123");
        var entity2 = OtherTestEntity.Create("123");

        // Act
        var hash1 = entity1.GetHashCode();
        var hash2 = entity2.GetHashCode();

        // Assert - Same ID but different types should have different hash codes
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void Entity_CanBeUsedInHashSet()
    {
        // Arrange
        var entity1 = TestEntity.Create("123");
        var entity2 = TestEntity.Create("123"); // Same ID
        var entity3 = TestEntity.Create("456"); // Different ID

        // Act
        var set = new HashSet<TestEntity> { entity1, entity2, entity3 };

        // Assert - entity2 should not be added since it equals entity1
        set.Should().HaveCount(2);
        set.Should().Contain(entity1);
        set.Should().Contain(entity3);
    }

    [Fact]
    public void Entity_CanBeUsedAsDictionaryKey()
    {
        // Arrange
        var entity1 = TestEntity.Create("123");
        var entity2 = TestEntity.Create("123"); // Same ID

        // Act
        var dict = new Dictionary<TestEntity, string>
        {
            [entity1] = "first"
        };
        dict[entity2] = "second"; // Should update the same key

        // Assert
        dict.Should().HaveCount(1);
        dict[entity1].Should().Be("second");
    }

    #endregion

    #region Equality Tests

    [Fact]
    public void Entity_WithSameId_AreEqual()
    {
        // Arrange
        var entity1 = TestEntity.Create("123");
        var entity2 = TestEntity.Create("123");

        // Act & Assert
        entity1.Should().Be(entity2);
        (entity1 == entity2).Should().BeTrue();
        (entity1 != entity2).Should().BeFalse();
        entity1.Equals(entity2).Should().BeTrue();
    }

    [Fact]
    public void Entity_WithDifferentIds_AreNotEqual()
    {
        // Arrange
        var entity1 = TestEntity.Create("123");
        var entity2 = TestEntity.Create("456");

        // Act & Assert
        entity1.Should().NotBe(entity2);
        (entity1 == entity2).Should().BeFalse();
        (entity1 != entity2).Should().BeTrue();
        entity1.Equals(entity2).Should().BeFalse();
    }

    [Fact]
    public void Entity_ComparedToNull_IsNotEqual()
    {
        // Arrange
        var entity = TestEntity.Create("123");

        // Act & Assert
        entity.Equals(null).Should().BeFalse();
        (entity == null).Should().BeFalse();
        (entity != null).Should().BeTrue();
    }

    [Fact]
    public void Entity_NullOnLeftSide_IsNotEqual()
    {
        // Arrange
        TestEntity? nullEntity = null;
        var entity = TestEntity.Create("123");

        // Act & Assert
        (nullEntity == entity).Should().BeFalse();
        (nullEntity != entity).Should().BeTrue();
    }

    [Fact]
    public void Entity_TwoNulls_AreEqual()
    {
        // Arrange
        TestEntity? entity1 = null;
        TestEntity? entity2 = null;

        // Act & Assert
        (entity1 == entity2).Should().BeTrue();
        (entity1 != entity2).Should().BeFalse();
    }

    [Fact]
    public void Entity_SameReference_AreEqual()
    {
        // Arrange
        var entity = TestEntity.Create("123");
        var sameRef = entity;

        // Act & Assert
        entity.Should().Be(sameRef);
        ReferenceEquals(entity, sameRef).Should().BeTrue();
    }

    [Fact]
    public void Entity_WithDefaultId_AreNotEqual()
    {
        // Arrange
        var entity1 = TestEntityWithDefaultableId.Create(Guid.Empty);
        var entity2 = TestEntityWithDefaultableId.Create(Guid.Empty);

        // Act & Assert - Entities with default IDs should not be equal (transient entities)
        entity1.Equals(entity2).Should().BeFalse();
    }

    [Fact]
    public void Entity_OneDefaultId_OneValidId_AreNotEqual()
    {
        // Arrange
        var transientEntity = TestEntityWithDefaultableId.Create(Guid.Empty);
        var persistedEntity = TestEntityWithDefaultableId.Create(Guid.NewGuid());

        // Act & Assert
        transientEntity.Equals(persistedEntity).Should().BeFalse();
        persistedEntity.Equals(transientEntity).Should().BeFalse();
    }

    [Fact]
    public void Entity_DifferentTypes_SameId_AreNotEqual()
    {
        // Arrange
        var entity1 = TestEntity.Create("123");
        var entity2 = OtherTestEntity.Create("123");

        // Act & Assert
        entity1.Equals(entity2).Should().BeFalse();
    }

    [Fact]
    public void Entity_ComparedToNonEntity_IsNotEqual()
    {
        // Arrange
        var entity = TestEntity.Create("123");
        object notAnEntity = "123";

        // Act & Assert
        entity.Equals(notAnEntity).Should().BeFalse();
    }

    [Fact]
    public void Entity_ComparedToDifferentObjectType_IsNotEqual()
    {
        // Arrange
        var entity = TestEntity.Create("123");
        object obj = new { Id = "123" };

        // Act & Assert
        entity.Equals(obj).Should().BeFalse();
    }

    #endregion

    #region Id Property Tests

    [Fact]
    public void Entity_Id_IsSetCorrectly()
    {
        // Arrange & Act
        var entity = TestEntity.Create("my-id");

        // Assert
        entity.Id.Should().Be("my-id");
    }

    [Fact]
    public void Entity_Id_WithGuidType_IsSetCorrectly()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        var entity = TestEntityWithDefaultableId.Create(id);

        // Assert
        entity.Id.Should().Be(id);
    }

    #endregion

    #region IEntity Interface Tests

    [Fact]
    public void Entity_ImplementsIEntity()
    {
        // Arrange
        var entity = TestEntity.Create("123");

        // Act & Assert
        entity.Should().BeAssignableTo<IEntity>();
    }

    [Fact]
    public void Entity_CreatedAt_DefaultsToDefault()
    {
        // Arrange & Act
        var entity = TestEntity.Create("123");

        // Assert
        entity.CreatedAt.Should().Be(default(DateTimeOffset));
    }

    [Fact]
    public void Entity_LastModified_DefaultsToDefault()
    {
        // Arrange & Act
        var entity = TestEntity.Create("123");

        // Assert
        entity.LastModified.Should().Be(default(DateTimeOffset));
    }

    [Fact]
    public void Entity_CreatedAt_CanBeSet()
    {
        // Arrange
        var entity = TestEntity.Create("123");
        var timestamp = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);

        // Act
        entity.CreatedAt = timestamp;

        // Assert
        entity.CreatedAt.Should().Be(timestamp);
    }

    [Fact]
    public void Entity_LastModified_CanBeSet()
    {
        // Arrange
        var entity = TestEntity.Create("123");
        var timestamp = new DateTimeOffset(2025, 6, 15, 10, 30, 0, TimeSpan.Zero);

        // Act
        entity.LastModified = timestamp;

        // Assert
        entity.LastModified.Should().Be(timestamp);
    }

    [Fact]
    public void Entity_Timestamps_AccessibleViaIEntity()
    {
        // Arrange
        var entity = TestEntity.Create("123");
        var created = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var modified = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        entity.CreatedAt = created;
        entity.LastModified = modified;

        // Assert — accessible through IEntity cast
        ((IEntity)entity).CreatedAt.Should().Be(created);
        ((IEntity)entity).LastModified.Should().Be(modified);
    }

    [Fact]
    public void Entity_Timestamps_SettableViaIEntity()
    {
        // Arrange
        var entity = TestEntity.Create("123");
        var timestamp = new DateTimeOffset(2025, 3, 15, 8, 0, 0, TimeSpan.Zero);

        // Act — simulates what the EF Core interceptor does
        ((IEntity)entity).CreatedAt = timestamp;
        ((IEntity)entity).LastModified = timestamp;

        // Assert
        entity.CreatedAt.Should().Be(timestamp);
        entity.LastModified.Should().Be(timestamp);
    }

    #endregion
}

#region Test Entities

internal class TestEntity : Entity<string>
{
    public string Name { get; }

    private TestEntity(string id, string name) : base(id) => Name = name;

    public static TestEntity Create(string id, string name = "Test") => new(id, name);
}

internal class OtherTestEntity : Entity<string>
{
    public string Description { get; }

    private OtherTestEntity(string id, string description) : base(id) => Description = description;

    public static OtherTestEntity Create(string id, string description = "Other") => new(id, description);
}

internal class TestEntityWithDefaultableId : Entity<Guid>
{
    private TestEntityWithDefaultableId(Guid id) : base(id) { }

    public static TestEntityWithDefaultableId Create(Guid id) => new(id);
}

#endregion