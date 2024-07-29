using AuctionService.Data;
using AuctionService.DTOs;
using AuctionService.Entities;
using AutoMapper;
using Contracts;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AuctionService;

/*
* Validate
*/
[ApiController]
[Route("api/auctions")]
public class AuctionsController : ControllerBase
{
    private readonly AuctionDbContext _context;
    private readonly IMapper _mapper;
    private readonly IPublishEndpoint _publishEndpoint;


    public AuctionsController(AuctionDbContext context, IMapper mapper, IPublishEndpoint publishEndpoint)
    {
        _context = context;
        _mapper = mapper;
        _publishEndpoint = publishEndpoint;

    }

    [HttpGet]
    public async Task<ActionResult<List<AuctionDTO>>> GetAllAuctions()
    {
        var auctions = await _context.Auctions
        .Include(x => x.Item)
        .OrderBy(x => x.Item.Make)
        .ToListAsync();
        return _mapper.Map<List<AuctionDTO>>(auctions);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AuctionDTO>> GetAuctionById(Guid id)
    {
        var auction = await _context.Auctions
        .Include(x => x.Item)
        .FirstOrDefaultAsync(x => x.Id == id);
        if (auction == null)
        {
            return NotFound();
        }
        return _mapper.Map<AuctionDTO>(auction);
    }

    [HttpPost]
    public async Task<ActionResult<AuctionDTO>> CreateAuction(CreateAuctionDTO auctionDTO)
    {
        var auction = _mapper.Map<Auction>(auctionDTO);
        auction.Seller = "test";
        _context.Auctions.Add(auction);

        // Publish to RabbitMQ
        var newAuction = _mapper.Map<AuctionDTO>(auction);
        await _publishEndpoint.Publish(_mapper.Map<AuctionCreated>(newAuction));

        // Save to DB
        var res = await _context.SaveChangesAsync() > 0;


        if (!res) return BadRequest("Failed to create auction in DB");
        return CreatedAtAction(nameof(GetAuctionById), new { auction.Id }, newAuction);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateAuction(Guid id, UpdateAuctionDTO auctionDTO)
    {
        var auction = await _context.Auctions.Include(x => x.Item).FirstOrDefaultAsync(x => x.Id == id);

        if (auction == null) return NotFound();

        auction.Item.Make = auctionDTO.Make ?? auction.Item.Make;
        auction.Item.Model = auctionDTO.Model ?? auction.Item.Model;
        auction.Item.Year = auctionDTO.Year ?? auction.Item.Year;
        auction.Item.Color = auctionDTO.Color ?? auction.Item.Color;
        auction.Item.Mileage = auctionDTO.Mileage ?? auction.Item.Mileage;

        // Publish to RabbitMQ
        await _publishEndpoint.Publish(_mapper.Map<AuctionUpdated>(auction));

        // Save to DB
        var res = await _context.SaveChangesAsync() > 0;

        if (res) return Ok();

        return BadRequest("Problem saving changes");
    }

    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteAuction(Guid id)
    {
        var auction = await _context.Auctions.FirstOrDefaultAsync(x => x.Id == id);

        if (auction == null) return NotFound();


        _context.Remove(auction);

        // Publish to RabbitMQ
        await _publishEndpoint.Publish<AuctionDeleted>(new { Id = auction.Id.ToString() });

        var res = await _context.SaveChangesAsync() > 0;

        if (!res) return BadRequest("Failed to delete auction");

        return Ok();
    }
}
