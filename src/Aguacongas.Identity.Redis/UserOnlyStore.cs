// Project: aguacongas/Identity.Firebase
// Copyright (c) 2020 @Olivier Lefebvre
using Microsoft.AspNetCore.Identity;
using Newtonsoft.Json;
using StackExchange.Redis;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Aguacongas.Identity.Redis
{
    /// <summary>
    /// Represents a new instance of a persistence store for <see cref="IdentityUser"/>.
    /// </summary>
    public class UserOnlyStore : UserOnlyStore<string>
    {
        /// <summary>
        /// Constructs a new instance of <see cref="UserStore{TUser, TRole, TKey}"/>.
        /// </summary>
        /// <param name="db">The <see cref="IDatabase"/>.</param>
        /// <param name="describer">The <see cref="IdentityErrorDescriber"/>.</param>
        public UserOnlyStore(IDatabase db, IdentityErrorDescriber describer = null) : base(db, describer) { }
    }

    /// <summary>
    /// Represents a new instance of a persistence store for <see cref="IdentityUser"/>.
    /// </summary>
    public class UserOnlyStore<TKey>: UserOnlyStore<IdentityUser<TKey>, TKey>
        where TKey : IEquatable<TKey>
    {
        /// <summary>
        /// Constructs a new instance of <see cref="UserStore{TUser, TRole, TKey}"/>.
        /// </summary>
        /// <param name="db">The <see cref="IDatabase"/>.</param>
        /// <param name="describer">The <see cref="IdentityErrorDescriber"/>.</param>
        public UserOnlyStore(IDatabase db, IdentityErrorDescriber describer = null) : base(db, describer) { }
    }

    /// <summary>
    /// Represents a new instance of a persistence store for the specified user and role types.
    /// </summary>
    /// <typeparam name="TUser">The type representing a user.</typeparam>
    public class UserOnlyStore<TUser, TKey> : UserOnlyStore<TUser, TKey, IdentityUserClaim<TKey>, IdentityUserLogin<TKey>, IdentityUserToken<TKey>>
        where TUser : IdentityUser<TKey>
        where TKey : IEquatable<TKey>
    {
        /// <summary>
        /// Constructs a new instance of <see cref="UserStore{TUser, TRole, TKey}"/>.
        /// </summary>
        /// <param name="db">The <see cref="IDatabase"/>.</param>
        /// <param name="describer">The <see cref="IdentityErrorDescriber"/>.</param>
        public UserOnlyStore(IDatabase db, IdentityErrorDescriber describer = null) : base(db, describer) { }
    }

    /// <summary>
    /// Represents a new instance of a persistence store for the specified user and role types.
    /// </summary>
    /// <typeparam name="TUser">The type representing a user.</typeparam>
    /// <typeparam name="TUserClaim">The type representing a claim.</typeparam>
    /// <typeparam name="TUserLogin">The type representing a user external login.</typeparam>
    /// <typeparam name="TUserToken">The type representing a user token.</typeparam>
    [SuppressMessage("Major Code Smell", "S2326:Unused type parameters should be removed", Justification = "Identity store implementation")]
    [SuppressMessage("Major Code Smell", "S3881:\"IDisposable\" should be implemented correctly", Justification = "Nothing to dispose")]
    [SuppressMessage("Major Code Smell", "S2436:Types and methods should not have too many generic parameters", Justification = "Identity store implementation")]
    [SuppressMessage("Design", "CA1063:Implement IDisposable Correctly", Justification = "Nothing to dispose")]
    [SuppressMessage("Critical Code Smell", "S1006:Method overrides should not change parameter defaults", Justification = "<Pending>")]
    public class UserOnlyStore<TUser, TKey, TUserClaim, TUserLogin, TUserToken> :
        RedisUserStoreBase<TUser, TKey, TUserClaim, TUserLogin, TUserToken>,
        IUserLoginStore<TUser>,
        IUserClaimStore<TUser>,
        IUserPasswordStore<TUser>,
        IUserSecurityStampStore<TUser>,
        IUserEmailStore<TUser>,
        IUserLockoutStore<TUser>,
        IUserPhoneNumberStore<TUser>,
        IUserTwoFactorStore<TUser>,
        IUserAuthenticationTokenStore<TUser>,
        IUserAuthenticatorKeyStore<TUser>,
        IUserTwoFactorRecoveryCodeStore<TUser>
        where TUser : IdentityUser<TKey>
        where TKey : IEquatable<TKey>
        where TUserClaim : IdentityUserClaim<TKey>, new()
        where TUserLogin : IdentityUserLogin<TKey>, new()
        where TUserToken : IdentityUserToken<TKey>, new()
    {
        private const string UsersRedisKey = "{users}";
        private const string UsersConcurencyStampIndexKey = "{users}-concurency";
        private const string UsersNameIndexKey = "{users}-name";
        private const string UsersEmailIndexKey = "{users}-email";
        private const string UserLoginsRedisKey = "{user}-logins";
        private const string UserLoginProviderKeyPrefix = "{user}-login-provider-";
        private const string UserClaimsRedisKey = "{user}-claims";
        private const string UserClaimsKeyPrefix = "{user}-claim-";
        private const string UserTokensRedisKey = "{user}-tokens";

        private readonly IDatabase _db;

        /// <summary>
        /// A navigation property for the users the store contains.
        /// </summary>
        public override IQueryable<TUser> Users
        {
            get
            {
                var results = _db.HashGetAll(UsersRedisKey);
                return results.Select(u => JsonConvert.DeserializeObject<TUser>(u.Value))
                    .AsQueryable();
            }
        }

        /// <summary>
        /// Creates a new instance of the store.
        /// </summary>
        /// <param name="db">The <see cref="IDatabase"/>.</param>
        /// <param name="describer">The <see cref="IdentityErrorDescriber"/> used to describe store errors.</param>
        public UserOnlyStore(IDatabase db, IdentityErrorDescriber describer = null) : base(describer ?? new IdentityErrorDescriber())
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// Creates the specified <paramref name="user"/> in the user store.
        /// </summary>
        /// <param name="user">The user to create.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation, containing the <see cref="IdentityResult"/> of the creation operation.</returns>
        public async override Task<IdentityResult> CreateAsync(TUser user, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            AssertNotNull(user, nameof(user));

            var userId = ConvertIdToString(user.Id);

            user.ConcurrencyStamp = "0";
            var tran = _db.CreateTransaction();
#pragma warning disable IDE0059 // Unnecessary assignment of a value
#pragma warning disable S1854 // Dead stores should be removed
            var userNotExistsCondition = tran.AddCondition(Condition.HashNotExists(UsersRedisKey, userId));
#pragma warning restore S1854 // Dead stores should be removed
#pragma warning restore IDE0059 // Unnecessary assignment of a value
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            tran.HashSetAsync(UsersRedisKey, userId, JsonConvert.SerializeObject(user));
            tran.HashSetAsync(UsersConcurencyStampIndexKey, userId, GetConcurrencyStamp(user));
            tran.HashSetAsync(UsersNameIndexKey, user.NormalizedUserName, userId);
            if (!string.IsNullOrEmpty(user.NormalizedEmail))
            {
                tran.HashSetAsync(UsersEmailIndexKey, user.NormalizedEmail, userId);
            }
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            if (!await tran.ExecuteAsync().ConfigureAwait(false))
            {
                return IdentityResult.Failed(new IdentityError
                {
                    Code = nameof(userNotExistsCondition),
                    Description = $"User id: {user.Id} already exists"
                });
            }

            return IdentityResult.Success;
        }

        /// <summary>
        /// Updates the specified <paramref name="user"/> in the user store.
        /// </summary>
        /// <param name="user">The user to update.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation, containing the <see cref="IdentityResult"/> of the update operation.</returns>
        public async override Task<IdentityResult> UpdateAsync(TUser user, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            AssertNotNull(user, nameof(user));

            var userId = ConvertIdToString(user.Id);

            var tran = _db.CreateTransaction();
            tran.AddCondition(Condition.HashEqual(UsersConcurencyStampIndexKey, userId, GetConcurrencyStamp(user)));
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            tran.HashSetAsync(UsersRedisKey, userId, JsonConvert.SerializeObject(user));
            var concurency = tran.HashIncrementAsync(UsersConcurencyStampIndexKey, userId);
            tran.HashSetAsync(UsersNameIndexKey, user.NormalizedUserName, userId);
            if (!string.IsNullOrEmpty(user.NormalizedEmail))
            {
                tran.HashSetAsync(UsersEmailIndexKey, user.NormalizedEmail, userId);
            }
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            if (!await tran.ExecuteAsync().ConfigureAwait(false))
            {
                return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
            }
            user.ConcurrencyStamp = concurency.Result.ToString();

            return IdentityResult.Success;
        }

        /// <summary>
        /// Deletes the specified <paramref name="user"/> from the user store.
        /// </summary>
        /// <param name="user">The user to delete.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation, containing the <see cref="IdentityResult"/> of the update operation.</returns>
        public async override Task<IdentityResult> DeleteAsync(TUser user, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            AssertNotNull(user, nameof(user));

            var userId = ConvertIdToString(user.Id);

            var tran = _db.CreateTransaction();
#pragma warning disable S1481 // Unused local variables should be removed
#pragma warning disable IDE0059 // Unnecessary assignment of a value
            var concurencyStampMatchCondition = tran.AddCondition(Condition.HashEqual(UsersConcurencyStampIndexKey, userId, GetConcurrencyStamp(user)));
#pragma warning restore IDE0059 // Unnecessary assignment of a value
#pragma warning restore S1481 // Unused local variables should be removed
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            tran.HashDeleteAsync(UsersRedisKey, userId);
            tran.HashDeleteAsync(UsersNameIndexKey, user.NormalizedUserName);
            if (user.NormalizedEmail != null)
            {
                tran.HashDeleteAsync(UsersNameIndexKey, user.NormalizedEmail);
            }
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            if (!await tran.ExecuteAsync().ConfigureAwait(false))
            {
                return IdentityResult.Failed(ErrorDescriber.ConcurrencyFailure());
            }

            return IdentityResult.Success;
        }

        public async override Task SetNormalizedUserNameAsync(TUser user, string normalizedName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            AssertNotNull(user, nameof(user));

            if (user.NormalizedEmail == normalizedName)
            {
                return;
            }

            var userId = ConvertIdToString(user.Id);

            var tran = _db.CreateTransaction();
            tran.AddCondition(Condition.HashEqual(UsersConcurencyStampIndexKey, userId, GetConcurrencyStamp(user)));
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            if (user.NormalizedUserName != null)
            {
                tran.HashDeleteAsync(UsersNameIndexKey, user.NormalizedUserName);
            }
            if (normalizedName != null)
            {
                tran.HashSetAsync(UsersNameIndexKey, normalizedName, userId);
            }
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            if (!await tran.ExecuteAsync().ConfigureAwait(false))
            {
                throw new DBConcurrencyException($"ConcurrencyStamp {user.ConcurrencyStamp} doesn't match for user: {user.Id}");
            }

            user.NormalizedUserName = normalizedName;
        }

        public async override Task SetNormalizedEmailAsync(TUser user, string normalizedEmail, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            AssertNotNull(user, nameof(user));

            if (user.NormalizedEmail == normalizedEmail)
            {
                return;
            }

            var userId = ConvertIdToString(user.Id);

            var tran = _db.CreateTransaction();
            tran.AddCondition(Condition.HashEqual(UsersConcurencyStampIndexKey, userId, GetConcurrencyStamp(user)));
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            if (user.NormalizedEmail != null)
            {
                tran.HashDeleteAsync(UsersEmailIndexKey, user.NormalizedEmail);
            }
            if (normalizedEmail != null)
            {
                tran.HashSetAsync(UsersEmailIndexKey, normalizedEmail, userId);
            }
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            if (!await tran.ExecuteAsync().ConfigureAwait(false))
            {
                throw new DBConcurrencyException($"ConcurrencyStamp {user.ConcurrencyStamp} doesn't match for user: {user.Id}");
            }
            user.NormalizedEmail = normalizedEmail;
        }

        /// <summary>
        /// Finds and returns a user, if any, who has the specified <paramref name="userId"/>.
        /// </summary>
        /// <param name="userId">The user ID to search for.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>
        /// The <see cref="Task"/> that represents the asynchronous operation, containing the user matching the specified <paramref name="userId"/> if it exists.
        /// </returns>
        public override async Task<TUser> FindByIdAsync(string userId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            var response = await _db.HashGetAsync(UsersRedisKey, userId).ConfigureAwait(false);
            if (response.HasValue)
            {
                var user = JsonConvert.DeserializeObject<TUser>(response);
                user.ConcurrencyStamp = (await _db.HashGetAsync(UsersConcurencyStampIndexKey, userId)
                    .ConfigureAwait(false)).ToString();

                return user;
            }
            return default;
        }

        /// <summary>
        /// Finds and returns a user, if any, who has the specified normalized user name.
        /// </summary>
        /// <param name="normalizedUserName">The normalized user name to search for.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>
        /// The <see cref="Task"/> that represents the asynchronous operation, containing the user matching the specified <paramref name="normalizedUserName"/> if it exists.
        /// </returns>
        public override async Task<TUser> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            var userId = await _db.HashGetAsync(UsersNameIndexKey, normalizedUserName).ConfigureAwait(false);
            if (!userId.HasValue)
            {
                return default;
            }

            return await FindByIdAsync(userId, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Get the claims associated with the specified <paramref name="user"/> as an asynchronous operation.
        /// </summary>
        /// <param name="user">The user whose claims should be retrieved.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>A <see cref="Task{TResult}"/> that contains the claims granted to a user.</returns>
        public async override Task<IList<Claim>> GetClaimsAsync(TUser user, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            AssertNotNull(user, nameof(user));

            var userId = ConvertIdToString(user.Id);

            var response = await _db.HashGetAsync(UserClaimsRedisKey, userId).ConfigureAwait(false);
            if (response.HasValue)
            {
                var claims = JsonConvert.DeserializeObject<List<TUserClaim>>(response);
                return claims.Select(c => c.ToClaim()).ToList();
            }

            return new List<Claim>(0);
        }

        /// <summary>
        /// Adds the <paramref name="claims"/> given to the specified <paramref name="user"/>.
        /// </summary>
        /// <param name="user">The user to add the claim to.</param>
        /// <param name="claims">The claim to add to the user.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation.</returns>
        public override async Task AddClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            AssertNotNull(user, nameof(user));
            AssertNotNull(claims, nameof(claims));

            var userClaims = await GetUserClaimsAsync(user).ConfigureAwait(false);

            userClaims.AddRange(claims.Select(c => CreateUserClaim(user, c)));

            var userId = ConvertIdToString(user.Id);

            var taskList = new List<Task>(claims.Count() + 1)
            {
                _db.HashSetAsync(UserClaimsRedisKey, userId, JsonConvert.SerializeObject(userClaims))
            };
            foreach (var claim in claims)
            {
                taskList.Add(_db.HashSetAsync(UserClaimsKeyPrefix + claim.Type, userId, claim.Value));
            }

            await Task.WhenAll(taskList).ConfigureAwait(false);
        }

        /// <summary>
        /// Replaces the <paramref name="claim"/> on the specified <paramref name="user"/>, with the <paramref name="newClaim"/>.
        /// </summary>
        /// <param name="user">The user to replace the claim on.</param>
        /// <param name="claim">The claim replace.</param>
        /// <param name="newClaim">The new claim replacing the <paramref name="claim"/>.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation.</returns>
        public async override Task ReplaceClaimAsync(TUser user, Claim claim, Claim newClaim, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            AssertNotNull(user, nameof(user));
            AssertNotNull(claim, nameof(claim));
            AssertNotNull(newClaim, nameof(newClaim));

            var userId = ConvertIdToString(user.Id);

            var userClaims = await GetUserClaimsAsync(user).ConfigureAwait(false);
            var taskList = new List<Task>(3);
            await Task.WhenAll(taskList).ConfigureAwait(false);
            foreach (var uc in userClaims)
            {
                if (uc.ClaimType == claim.Type && uc.ClaimValue == claim.Value)
                {
                    uc.ClaimType = newClaim.Type;
                    uc.ClaimValue = newClaim.Value;
                    taskList.Add(_db.HashDeleteAsync(UserClaimsKeyPrefix + claim.Type, userId));
                    taskList.Add(_db.HashSetAsync(UserClaimsKeyPrefix + newClaim.Type, userId, newClaim.Value));
                }
            }

            taskList.Add(_db.HashSetAsync(UserClaimsRedisKey, userId, JsonConvert.SerializeObject(userClaims)));
            await Task.WhenAll(taskList).ConfigureAwait(false);
        }

        /// <summary>
        /// Removes the <paramref name="claims"/> given from the specified <paramref name="user"/>.
        /// </summary>
        /// <param name="user">The user to remove the claims from.</param>
        /// <param name="claims">The claim to remove.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation.</returns>
        public async override Task RemoveClaimsAsync(TUser user, IEnumerable<Claim> claims, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            AssertNotNull(user, nameof(user));
            AssertNotNull(claims, nameof(claims));

            var userId = ConvertIdToString(user.Id);

            var userClaims = await GetUserClaimsAsync(user).ConfigureAwait(false);
            var taskList = new List<Task>(claims.Count() + 1);
            foreach (var claim in claims)
            {
                userClaims.RemoveAll(uc => uc.ClaimType == claim.Type && uc.ClaimValue == claim.Value);
                taskList.Add(_db.HashDeleteAsync(UserClaimsKeyPrefix + claim.Type, userId));
            }

            taskList.Add(_db.HashSetAsync(UserClaimsRedisKey, userId, JsonConvert.SerializeObject(userClaims)));

            await Task.WhenAll(taskList).ConfigureAwait(false);
        }

        /// <summary>
        /// Adds the <paramref name="login"/> given to the specified <paramref name="user"/>.
        /// </summary>
        /// <param name="user">The user to add the login to.</param>
        /// <param name="login">The login to add to the user.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation.</returns>
        public override async Task AddLoginAsync(TUser user, UserLoginInfo login,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            AssertNotNull(user, nameof(user));
            AssertNotNull(login, nameof(login));

            var userId = ConvertIdToString(user.Id);

            var logins = await GetUserLoginsAsync(userId).ConfigureAwait(false);
            logins.Add(CreateUserLogin(user, login));

            await _db.HashSetAsync(UserLoginsRedisKey, userId, JsonConvert.SerializeObject(logins))
                .ConfigureAwait(false);
            await _db.HashSetAsync(UserLoginProviderKeyPrefix + login.LoginProvider, login.ProviderKey, userId)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Removes the <paramref name="loginProvider"/> given from the specified <paramref name="user"/>.
        /// </summary>
        /// <param name="user">The user to remove the login from.</param>
        /// <param name="loginProvider">The login to remove from the user.</param>
        /// <param name="providerKey">The key provided by the <paramref name="loginProvider"/> to identify a user.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The <see cref="Task"/> that represents the asynchronous operation.</returns>
        public override async Task RemoveLoginAsync(TUser user, string loginProvider, string providerKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            AssertNotNull(user, nameof(user));

            var userId = ConvertIdToString(user.Id);

            var logins = await GetUserLoginsAsync(userId).ConfigureAwait(false);
            logins.RemoveAll(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey);

            await _db.HashSetAsync(UserLoginsRedisKey, userId, JsonConvert.SerializeObject(logins))
                .ConfigureAwait(false);
            await _db.HashDeleteAsync(UserLoginProviderKeyPrefix + loginProvider, providerKey)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Retrieves the associated logins for the specified <param ref="user"/>.
        /// </summary>
        /// <param name="user">The user whose associated logins to retrieve.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>
        /// The <see cref="Task"/> for the asynchronous operation, containing a list of <see cref="UserLoginInfo"/> for the specified <paramref name="user"/>, if any.
        /// </returns>
        public async override Task<IList<UserLoginInfo>> GetLoginsAsync(TUser user, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            AssertNotNull(user, nameof(user));

            var userId = ConvertIdToString(user.Id);

            var logins = await GetUserLoginsAsync(userId).ConfigureAwait(false);

            return logins
                .Select(l => new UserLoginInfo(l.LoginProvider, l.ProviderKey, l.ProviderDisplayName))
                .ToList();
        }

        /// <summary>
        /// Retrieves the user associated with the specified login provider and login provider key.
        /// </summary>
        /// <param name="loginProvider">The login provider who provided the <paramref name="providerKey"/>.</param>
        /// <param name="providerKey">The key provided by the <paramref name="loginProvider"/> to identify a user.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>
        /// The <see cref="Task"/> for the asynchronous operation, containing the user, if any which matched the specified login provider and key.
        /// </returns>
        public async override Task<TUser> FindByLoginAsync(string loginProvider, string providerKey,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            var userLogin = await FindUserLoginAsync(loginProvider, providerKey, cancellationToken)
                .ConfigureAwait(false);
            if (userLogin != null)
            {
                return await FindUserAsync(userLogin.UserId, cancellationToken)
                    .ConfigureAwait(false);
            }
            return null;
        }

        /// <summary>
        /// Gets the user, if any, associated with the specified, normalized email address.
        /// </summary>
        /// <param name="normalizedEmail">The normalized email address to return the user for.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>
        /// The task object containing the results of the asynchronous lookup operation, the user if any associated with the specified normalized email address.
        /// </returns>
        public override async Task<TUser> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();

            var response = await _db.HashGetAsync(UsersEmailIndexKey, normalizedEmail)
                .ConfigureAwait(false);
            if (response.HasValue)
            {
                return await FindByIdAsync(response, cancellationToken).ConfigureAwait(false);
            }

            return default;
        }

        /// <summary>
        /// Retrieves all users with the specified claim.
        /// </summary>
        /// <param name="claim">The claim whose users should be retrieved.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>
        /// The <see cref="Task"/> contains a list of users, if any, that contain the specified claim. 
        /// </returns>
        public async override Task<IList<TUser>> GetUsersForClaimAsync(Claim claim, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            AssertNotNull(claim, nameof(claim));

            var result = await _db.HashGetAllAsync(UserClaimsKeyPrefix + claim.Type)
                .ConfigureAwait(false);

            var users = new ConcurrentBag<TUser>();
            var taskList = new List<Task>(result.Length);
            foreach (var uc in result)
            {
                taskList.Add(Task.Run(async () => {
                    var user = await FindByIdAsync(uc.Name, cancellationToken)
                        .ConfigureAwait(false);
                    if (user != null)
                    {
                        users.Add(user);
                    }
                }));
            }

            await Task.WhenAll(taskList.ToArray())
                .ConfigureAwait(false);

            return users.ToList();
        }

        /// <summary>
        /// Return a user login with the matching userId, provider, providerKey if it exists.
        /// </summary>
        /// <param name="userId">The user's id.</param>
        /// <param name="loginProvider">The login provider name.</param>
        /// <param name="providerKey">The key provided by the <paramref name="loginProvider"/> to identify a user.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The user login if it exists.</returns>
        internal Task<TUserLogin> FindUserLoginInternalAsync(string userId, string loginProvider, string providerKey, CancellationToken cancellationToken)
        {
            return FindUserLoginAsync(userId, loginProvider, providerKey, cancellationToken);
        }

        /// <summary>
        /// Return a user login with  provider, providerKey if it exists.
        /// </summary>
        /// <param name="loginProvider">The login provider name.</param>
        /// <param name="providerKey">The key provided by the <paramref name="loginProvider"/> to identify a user.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The user login if it exists.</returns>
        internal Task<TUserLogin> FindUserLoginInternalAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
        {
            return FindUserLoginAsync(loginProvider, providerKey, cancellationToken);
        }

        /// <summary>
        /// Get user tokens
        /// </summary>
        /// <param name="user">The token owner.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>User tokens.</returns>
        internal Task<List<TUserToken>> GetUserTokensInternalAsync(TUser user, CancellationToken cancellationToken)
        {
            return GetUserTokensAsync(user, cancellationToken);
        }

        /// <summary>
        /// Save user tokens.
        /// </summary>
        /// <param name="user">The tokens owner.</param>
        /// <param name="tokens">Tokens to save</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns></returns>
        internal Task SaveUserTokensInternalAsync(TUser user, IEnumerable<TUserToken> tokens, CancellationToken cancellationToken)
        {
            return SaveUserTokensAsync(user, tokens, cancellationToken);
        }
        
        /// <summary>
        /// Return a user with the matching userId if it exists.
        /// </summary>
        /// <param name="userId">The user's id.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The user if it exists.</returns>
        protected override Task<TUser> FindUserAsync(TKey userId, CancellationToken cancellationToken)
        {
            return FindByIdAsync(userId.ToString(), cancellationToken);
        }

        /// <summary>
        /// Return a user login with the matching userId, provider, providerKey if it exists.
        /// </summary>
        /// <param name="userId">The user's id.</param>
        /// <param name="loginProvider">The login provider name.</param>
        /// <param name="providerKey">The key provided by the <paramref name="loginProvider"/> to identify a user.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The user login if it exists.</returns>
        protected override async Task<TUserLogin> FindUserLoginAsync(string userId, string loginProvider, string providerKey, CancellationToken cancellationToken)
        {
            var data = await GetUserLoginsAsync(userId).ConfigureAwait(false);
            if (data != null)
            {
                return data.FirstOrDefault(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey);
            }
            return null;
        }

        /// <summary>
        /// Return a user login with  provider, providerKey if it exists.
        /// </summary>
        /// <param name="loginProvider">The login provider name.</param>
        /// <param name="providerKey">The key provided by the <paramref name="loginProvider"/> to identify a user.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>The user login if it exists.</returns>
        protected override async Task<TUserLogin> FindUserLoginAsync(string loginProvider, string providerKey, CancellationToken cancellationToken)
        {
            var userId = await _db.HashGetAsync(UserLoginProviderKeyPrefix + loginProvider, providerKey)
                .ConfigureAwait(false);

            if (userId.HasValue)
            {
                var logins = await GetUserLoginsAsync(userId)
                    .ConfigureAwait(false);
                return logins.FirstOrDefault(l => l.LoginProvider == loginProvider && l.ProviderKey == providerKey);
            }

            return default;
        }

        /// <summary>
        /// Get user tokens
        /// </summary>
        /// <param name="user">The token owner.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>User tokens.</returns>
        protected override async Task<List<TUserToken>> GetUserTokensAsync(TUser user, CancellationToken cancellationToken)
        {
            var userId = ConvertIdToString(user.Id);

            var result = await _db.HashGetAsync(UserTokensRedisKey, userId)
                .ConfigureAwait(false);
            if (result.HasValue)
            {
                return JsonConvert.DeserializeObject<List<TUserToken>>(result);
            }
            return new List<TUserToken>(0);
        }

        /// <summary>
        /// Save user tokens.
        /// </summary>
        /// <param name="user">The tokens owner.</param>
        /// <param name="tokens">Tokens to save</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns></returns>
        protected override async Task SaveUserTokensAsync(TUser user, IEnumerable<TUserToken> tokens, CancellationToken cancellationToken)
        {
            var userId = ConvertIdToString(user.Id);

            await _db.HashSetAsync(UserTokensRedisKey, userId, JsonConvert.SerializeObject(tokens))
                .ConfigureAwait(false);
        }

        protected virtual async Task<List<TUserClaim>> GetUserClaimsAsync(TUser user)
        {
            var userId = ConvertIdToString(user.Id);

            var response = await _db.HashGetAsync(UserClaimsRedisKey, userId)
                .ConfigureAwait(false);
            if (response.HasValue)
            {
                return JsonConvert.DeserializeObject<List<TUserClaim>>(response);
            }

            return new List<TUserClaim>();
        }

        protected virtual async Task<List<TUserLogin>> GetUserLoginsAsync(string userId)
        {
            var response = await _db.HashGetAsync(UserLoginsRedisKey, userId)
                .ConfigureAwait(false);
            if (response.HasValue)
            {
                return JsonConvert.DeserializeObject<List<TUserLogin>>(response);
            }

            return new List<TUserLogin>();
        }

        private static int? GetConcurrencyStamp(TUser user)
        {
            if (int.TryParse(user.ConcurrencyStamp, out int stamp))
            {
                return stamp;
            }
            return null;
        }
    }
}
