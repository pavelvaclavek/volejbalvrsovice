using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using VolejbalVrsovice.Core;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Repositories;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Db;

namespace VolejbalVrsovice.Data.Repositories
{
    public sealed class AppUserRepository : RepositoryBase, IAppUserRepository
    {
        internal const string InsertSql = @"
INSERT INTO AppUser (Email, DisplayName, Role, IsActive, MustChangePassword, LastLoginAt, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
VALUES (@Email, @DisplayName, @Role, @IsActive, @MustChangePassword, @LastLoginAt, @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy);
SELECT last_insert_rowid();";

        private const string SelectSql = @"
SELECT Id, Email, DisplayName, Role, IsActive, MustChangePassword, LastLoginAt, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
FROM AppUser";

        private const string UpdateSql = @"
UPDATE AppUser
SET Email = @Email, DisplayName = @DisplayName, Role = @Role, IsActive = @IsActive,
    MustChangePassword = @MustChangePassword, ModifiedAt = @ModifiedAt, ModifiedBy = @ModifiedBy
WHERE Id = @Id;";

        public AppUserRepository(IDatabase database, ICurrentUser currentUser, IClock clock)
            : base(database, currentUser, clock)
        {
        }

        public Task<AppUser> GetByIdAsync(int id)
        {
            return GetAsync<AppUser>(SelectSql + " WHERE Id = @id;", new { id });
        }

        public Task<AppUser> GetByEmailAsync(string email)
        {
            return GetAsync<AppUser>(SelectSql + " WHERE Email = @email;", new { email = EmailAddress.Normalize(email) });
        }

        public Task<IReadOnlyList<AppUser>> GetAllAsync()
        {
            return ListAsync<AppUser>(SelectSql + " ORDER BY Email;");
        }

        public Task<int> CountActiveAdminsAsync()
        {
            return Db.ReadAsync(connection => connection.ExecuteScalarAsync<int>(
                "SELECT count(*) FROM AppUser WHERE IsActive = 1 AND Role = @role;", new { role = UserRole.Admin }));
        }

        public Task InsertAsync(AppUser user)
        {
            Normalize(user);
            return InsertEntityAsync(InsertSql, user);
        }

        public Task UpdateAsync(AppUser user)
        {
            Normalize(user);
            return UpdateEntityAsync(UpdateSql, user);
        }

        public Task RecordLoginAsync(int userId)
        {
            return Db.WriteAsync((connection, transaction) => connection.ExecuteAsync(
                "UPDATE AppUser SET LastLoginAt = @now WHERE Id = @userId;", new { now = UtcNow(), userId }, transaction));
        }

        private static void Normalize(AppUser user)
        {
            user.Email = EmailAddress.Normalize(user.Email);
            user.DisplayName = Clean(user.DisplayName);
        }
    }
}
