using FluentAssertions;
using InfernalHierarchy.Core.Entities;
using InfernalHierarchy.Memory.Tenancy;
using Microsoft.Extensions.Logging;
using Moq;
using System.Threading;
using Xunit;

namespace InfernalHierarchy.Memory.Tests;

public sealed class TenantIsolationServiceTests
{
    [Fact]
    public async Task Constructor_ShouldInitializeDefaultTenant_AndSetCurrentTenant()
    {
        var root = CreateTempRoot();
        try
        {
            var svc = new TenantIsolationService(Mock.Of<ILogger<TenantIsolationService>>(), root);

            svc.GetCurrentTenant().Should().NotBeNull();
            svc.GetCurrentTenant()!.TenantId.Should().Be("default");

            var tenant = await svc.GetTenantAsync("default");
            tenant.Should().NotBeNull();
            tenant!.IsActive.Should().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task CreateTenantAsync_ShouldPersist_AndSetTierLimits()
    {
        var root = CreateTempRoot();
        try
        {
            var svc = new TenantIsolationService(Mock.Of<ILogger<TenantIsolationService>>(), root);

            var tenant = new TenantContext
            {
                TenantId = "t1",
                Name = "Tenant1",
                Tier = TenantTier.Basic,
                IsActive = true
            };

            await svc.CreateTenantAsync(tenant);

            var loaded = await svc.GetTenantAsync("t1");
            loaded.Should().NotBeNull();
            loaded!.DatabasePath.Should().NotBeNullOrWhiteSpace();
            loaded.MaxAgents.Should().Be(20);
            loaded.MaxMemoryEntries.Should().Be(10000);
            loaded.MaxTokensPerMonth.Should().Be(1000000);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SetCurrentTenantAsync_ShouldThrow_WhenTenantNotFound()
    {
        var root = CreateTempRoot();
        try
        {
            var svc = new TenantIsolationService(Mock.Of<ILogger<TenantIsolationService>>(), root);

            var act = async () => await svc.SetCurrentTenantAsync("missing");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task SetCurrentTenantAsync_ShouldThrow_WhenTenantInactive()
    {
        var root = CreateTempRoot();
        try
        {
            var svc = new TenantIsolationService(Mock.Of<ILogger<TenantIsolationService>>(), root);

            await svc.CreateTenantAsync(new TenantContext
            {
                TenantId = "t2",
                Name = "Tenant2",
                Tier = TenantTier.Free,
                IsActive = false
            });

            var act = async () => await svc.SetCurrentTenantAsync("t2");

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task DeleteTenantAsync_ShouldRemoveTenant_AndCacheEntry()
    {
        var root = CreateTempRoot();
        try
        {
            var svc = new TenantIsolationService(Mock.Of<ILogger<TenantIsolationService>>(), root);

            await svc.CreateTenantAsync(new TenantContext
            {
                TenantId = "t3",
                Name = "Tenant3",
                Tier = TenantTier.Free,
                IsActive = true
            });

            (await svc.GetTenantAsync("t3")).Should().NotBeNull();

            await svc.DeleteTenantAsync("t3");

            (await svc.GetTenantAsync("t3")).Should().BeNull();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task DeleteTenantAsync_ShouldNotThrow_WhenTenantMissing()
    {
        var root = CreateTempRoot();
        try
        {
            var svc = new TenantIsolationService(Mock.Of<ILogger<TenantIsolationService>>(), root);

            var act = async () => await svc.DeleteTenantAsync("missing");

            await act.Should().NotThrowAsync();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task UpdateTenantAsync_ShouldPersistChanges_AndRefreshCache()
    {
        var root = CreateTempRoot();
        try
        {
            var svc = new TenantIsolationService(Mock.Of<ILogger<TenantIsolationService>>(), root);

            var tenant = new TenantContext
            {
                TenantId = "t5",
                Name = "Tenant5",
                Tier = TenantTier.Free,
                IsActive = true
            };

            await svc.CreateTenantAsync(tenant);
            tenant.Name = "Tenant5-Updated";

            await svc.UpdateTenantAsync(tenant);

            var loaded = await svc.GetTenantAsync("t5");
            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be("Tenant5-Updated");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task GetActiveTenantsAsync_ShouldReturnOnlyActiveTenants()
    {
        var root = CreateTempRoot();
        try
        {
            var svc = new TenantIsolationService(Mock.Of<ILogger<TenantIsolationService>>(), root);

            await svc.CreateTenantAsync(new TenantContext
            {
                TenantId = "t6",
                Name = "Tenant6",
                Tier = TenantTier.Basic,
                IsActive = true
            });

            await svc.CreateTenantAsync(new TenantContext
            {
                TenantId = "t7",
                Name = "Tenant7",
                Tier = TenantTier.Basic,
                IsActive = false
            });

            var active = await svc.GetActiveTenantsAsync();

            active.Select(t => t.TenantId).Should().Contain("default");
            active.Select(t => t.TenantId).Should().Contain("t6");
            active.Select(t => t.TenantId).Should().NotContain("t7");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void CanPerformOperation_ShouldReturnFalse_WhenNoTenantContext()
    {
        var root = CreateTempRoot();
        try
        {
            var svc = new TenantIsolationService(Mock.Of<ILogger<TenantIsolationService>>(), root);

            // Clear current tenant via reflection (test-only).
            var asyncLocal = (AsyncLocal<TenantContext?>)typeof(TenantIsolationService)
                .GetField("_currentTenant", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(svc)!;
            asyncLocal.Value = null;

            svc.CanPerformOperation("llm_call").Should().BeFalse();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteInTenantContextAsync_ShouldInvokeAction_WhenTenantSet()
    {
        var root = CreateTempRoot();
        try
        {
            var svc = new TenantIsolationService(Mock.Of<ILogger<TenantIsolationService>>(), root);

            await svc.SetCurrentTenantAsync("default");

            var called = false;
            await svc.ExecuteInTenantContextAsync(_ =>
            {
                called = true;
                return Task.CompletedTask;
            });

            called.Should().BeTrue();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteInTenantContextAsync_ShouldThrow_WhenNoTenantSet()
    {
        var root = CreateTempRoot();
        try
        {
            var svc = new TenantIsolationService(Mock.Of<ILogger<TenantIsolationService>>(), root);

            svc.GetCurrentTenant().Should().NotBeNull();

            await svc.SetCurrentTenantAsync("default");

            await svc.CreateTenantAsync(new TenantContext
            {
                TenantId = "t4",
                Name = "Tenant4",
                Tier = TenantTier.Free,
                IsActive = true
            });

            // Clear current tenant via reflection (test-only).
            var asyncLocal = (AsyncLocal<TenantContext?>)typeof(TenantIsolationService)
                .GetField("_currentTenant", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(svc)!;
            asyncLocal.Value = null;

            var act = async () => await svc.ExecuteInTenantContextAsync(_ => Task.CompletedTask);

            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task ExecuteInTenantContextAsync_ShouldRethrow_WhenActionThrows()
    {
        var root = CreateTempRoot();
        try
        {
            var svc = new TenantIsolationService(Mock.Of<ILogger<TenantIsolationService>>(), root);
            await svc.SetCurrentTenantAsync("default");

            var act = async () => await svc.ExecuteInTenantContextAsync(_ =>
                Task.FromException(new InvalidOperationException("boom")));

            await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*boom*");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "InfernalHierarchy", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDeleteDirectory(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
