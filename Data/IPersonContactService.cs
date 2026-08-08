using Sati.Models;

namespace Sati.Data
{
    public interface IPersonContactService
    {
        Task<List<PersonContact>> GetActiveByPersonAsync(int personId);
        Task<PersonContact> SaveAsync(PersonContact contact);
        Task ArchiveAsync(int contactId);
    }
}
