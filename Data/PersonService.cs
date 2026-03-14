using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Text;

namespace Proofer.Data
{
    public class PersonService : IPersonService
    {
        private readonly ProoferContext _context;

        //consstructor
        public PersonService(ProoferContext context)
        {
            _context = context;
        }

        // methods
        public async Task<Person> AddPersonAsync(Person person)
        {
            _context.People.Add(person);
            await _context.SaveChangesAsync();
            return person;
 
        }

        public async Task DeletePersonAsync(Person person)
        {
            _context.People.Remove(person);
            await _context.SaveChangesAsync();
        }

        public async Task<List<Person>> GetAllPeopleAsync()
        {
            return await _context.People
                .Include(p => p.Notes)
                .Include(p => p.Forms)
                .ToListAsync();
            ;

        }
    }
}
