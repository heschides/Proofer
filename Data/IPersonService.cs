using System;
using System.Collections.Generic;
using System.Text;

namespace Sati.Data
{
    public interface IPersonService
    {
        Task<Person> AddPersonAsync(Person person);
        Task DeletePersonAsync(Person person);
        Task<List<Person>> GetAllPeopleAsync(int userId);
        Task<Person> EditPersonAsync(Person person);
    }
}
