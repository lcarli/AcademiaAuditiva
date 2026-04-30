using AcademiaAuditiva.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace AcademiaAuditiva.Services
{
    /// <summary>
    /// Seeds Identity roles (Admin/Teacher/Student) and creates the
    /// bootstrap admin user from configuration. Idempotent: safe to run
    /// on every application start.
    /// </summary>
    public class IdentityBootstrapper
    {
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AdminBootstrapOptions _adminOptions;
        private readonly ILogger<IdentityBootstrapper> _logger;

        public IdentityBootstrapper(
            RoleManager<IdentityRole> roleManager,
            UserManager<ApplicationUser> userManager,
            IOptions<AdminBootstrapOptions> adminOptions,
            ILogger<IdentityBootstrapper> logger)
        {
            _roleManager = roleManager;
            _userManager = userManager;
            _adminOptions = adminOptions.Value;
            _logger = logger;
        }

        public async Task RunAsync()
        {
            await EnsureRolesAsync();
            await EnsureBootstrapAdminAsync();
        }

        private async Task EnsureRolesAsync()
        {
            foreach (var role in RoleNames.All)
            {
                if (!await _roleManager.RoleExistsAsync(role))
                {
                    var result = await _roleManager.CreateAsync(new IdentityRole(role));
                    if (result.Succeeded)
                    {
                        _logger.LogInformation("Created Identity role {Role}", role);
                    }
                    else
                    {
                        _logger.LogError("Failed to create role {Role}: {Errors}",
                            role, string.Join("; ", result.Errors));
                    }
                }
            }
        }

        private async Task EnsureBootstrapAdminAsync()
        {
            if (string.IsNullOrWhiteSpace(_adminOptions.Email))
            {
                _logger.LogWarning("Admin:Email is not configured; skipping bootstrap admin creation.");
                return;
            }

            var user = await _userManager.FindByEmailAsync(_adminOptions.Email);

            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = _adminOptions.Email,
                    Email = _adminOptions.Email,
                    EmailConfirmed = true,
                    FirstName = _adminOptions.FirstName,
                    LastName = _adminOptions.LastName,
                    LockoutEnabled = true,
                };

                IdentityResult createResult;
                if (!string.IsNullOrWhiteSpace(_adminOptions.InitialPassword))
                {
                    createResult = await _userManager.CreateAsync(user, _adminOptions.InitialPassword);
                }
                else
                {
                    // No password configured: create the account anyway so
                    // forgot-password can be used. We assign a strong random
                    // password the operator never sees.
                    var temp = $"{Guid.NewGuid():N}!Aa1";
                    createResult = await _userManager.CreateAsync(user, temp);
                }

                if (!createResult.Succeeded)
                {
                    _logger.LogError("Failed to create bootstrap admin {Email}: {Errors}",
                        _adminOptions.Email, string.Join("; ", createResult.Errors));
                    return;
                }

                _logger.LogInformation("Created bootstrap admin {Email}", _adminOptions.Email);
            }

            if (!await _userManager.IsInRoleAsync(user, RoleNames.Admin))
            {
                var addRoleResult = await _userManager.AddToRoleAsync(user, RoleNames.Admin);
                if (addRoleResult.Succeeded)
                {
                    _logger.LogInformation("Granted Admin role to {Email}", _adminOptions.Email);
                }
                else
                {
                    _logger.LogError("Failed to grant Admin role to {Email}: {Errors}",
                        _adminOptions.Email, string.Join("; ", addRoleResult.Errors));
                }
            }
        }
    }
}
