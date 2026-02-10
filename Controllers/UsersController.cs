using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SkinAI.API.Data;
using SkinAI.API.Dtos; // ← هنضيف الـ DTO هنا
using SkinAI.API.Models;
using System.Linq;
using System.Threading.Tasks;

namespace SkinAI.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        public UsersController(ApplicationDbContext context)
        {
            _context = context;
        }


        // GET ALL USERS
        [HttpGet]
        public async Task<IActionResult> GetUsers()
        {
            return Ok(await _context.Users.ToListAsync());
        }


        // GET USER BY ID
        [HttpGet("{id}")]
        public async Task<IActionResult> GetUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            return Ok(user);
        }


        // CREATE USER
        [HttpPost]
        public async Task<IActionResult> CreateUser(User user)
        {
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return Ok(user);
        }


        // UPDATE USER
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, User updated)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();


            user.FullName = updated.FullName;
            
            user.Email = updated.Email;
            user.PasswordHash = updated.PasswordHash;
            user.Role = updated.Role;


            await _context.SaveChangesAsync();
            return Ok(user);
        }


        // DELETE USER
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();


            _context.Users.Remove(user);
            await _context.SaveChangesAsync();


            return Ok("Deleted successfully");
        }
    }
    
}
