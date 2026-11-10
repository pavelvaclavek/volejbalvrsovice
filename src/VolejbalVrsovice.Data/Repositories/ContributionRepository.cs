using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using VolejbalVrsovice.Core.Entities;
using VolejbalVrsovice.Core.Repositories;
using VolejbalVrsovice.Core.Services;
using VolejbalVrsovice.Data.Db;

namespace VolejbalVrsovice.Data.Repositories
{
    public sealed class ContributionRepository : RepositoryBase, IContributionRepository
    {
        private const string SelectSql = @"
SELECT Id, PlayerId, Date, Amount, PaymentMethod, VariableSymbol, BankReference, Note, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy
FROM Contribution";

        private const string InsertSql = @"
INSERT INTO Contribution (PlayerId, Date, Amount, PaymentMethod, VariableSymbol, BankReference, Note, CreatedAt, CreatedBy, ModifiedAt, ModifiedBy)
VALUES (@PlayerId, @Date, @Amount, @PaymentMethod, @VariableSymbol, @BankReference, @Note, @CreatedAt, @CreatedBy, @ModifiedAt, @ModifiedBy);
SELECT last_insert_rowid();";

        private const string UpdateSql = @"
UPDATE Contribution
SET PlayerId = @PlayerId, Date = @Date, Amount = @Amount, PaymentMethod = @PaymentMethod,
    VariableSymbol = @VariableSymbol, BankReference = @BankReference, Note = @Note,
    ModifiedAt = @ModifiedAt, ModifiedBy = @ModifiedBy
WHERE Id = @Id;";

        public ContributionRepository(IDatabase database, ICurrentUser currentUser, IClock clock)
            : base(database, currentUser, clock)
        {
        }

        public Task<Contribution> GetByIdAsync(int id)
        {
            return GetAsync<Contribution>(SelectSql + " WHERE Id = @id;", new { id });
        }

        public Task<IReadOnlyList<Contribution>> GetByPlayerAsync(int playerId)
        {
            return ListAsync<Contribution>(SelectSql + " WHERE PlayerId = @playerId ORDER BY Date DESC, Id DESC;", new { playerId });
        }

        public Task<bool> HasBalanceTransferAsync(int playerId)
        {
            return Db.ReadAsync(async connection =>
                await connection.ExecuteScalarAsync<long>(
                    "SELECT count(*) FROM Contribution WHERE PlayerId = @playerId AND PaymentMethod = @method;",
                    new { playerId, method = PaymentMethod.BalanceTransfer }).ConfigureAwait(false) > 0);
        }

        public Task InsertAsync(Contribution contribution)
        {
            Normalize(contribution);
            return InsertEntityAsync(InsertSql, contribution);
        }

        public Task UpdateAsync(Contribution contribution)
        {
            Normalize(contribution);
            return UpdateEntityAsync(UpdateSql, contribution);
        }

        public Task DeleteAsync(int id)
        {
            return DeleteEntityAsync("DELETE FROM Contribution WHERE Id = @id;", nameof(Contribution), id);
        }

        private static void Normalize(Contribution contribution)
        {
            contribution.VariableSymbol = Clean(contribution.VariableSymbol);
            contribution.BankReference = Clean(contribution.BankReference);
            contribution.Note = Clean(contribution.Note);
        }
    }
}
