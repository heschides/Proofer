using System;
using System.Collections.Generic;
using System.Text;

namespace Proofer.Data
{
    public interface IPersonService
    {
        Task<Person> AddPersonAsync(Person person);
        Task DeletePersonAsync(Person person);
        Task<List<Person>> GetAllPeopleAsync();
        Task<Person> EditPersonAsync(Person person);
    }
}
