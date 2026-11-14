using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Runix.Core.Models;

namespace Runix.Core.Interfaces;

public interface IProgrammabilityService
{
    Task<List<DatabaseObjectInfo>> GetProgrammableObjectsAsync(DatabaseObjectType? filterType = null, CancellationToken cancellationToken = default);
    Task<string> GetObjectDdlAsync(DatabaseObjectInfo obj, CancellationToken cancellationToken = default);
    Task<QueryResult> ApplyObjectDdlAsync(DatabaseObjectInfo obj, string newDdl, bool isExistingObject = true, CancellationToken cancellationToken = default);
    Task<QueryResult> DropObjectAsync(DatabaseObjectInfo obj, CancellationToken cancellationToken = default);
    string GenerateTemplateDdl(DatabaseObjectType objectType, string objectName = "new_object", string? targetTable = null);
}
