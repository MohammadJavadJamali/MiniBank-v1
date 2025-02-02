﻿using Domain.Model;
using Domain.DTO.Node;
using Application.Helpers;
using Application.Repository;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace API.Controllers
{
    [Route("api/[controller]")]
    [Authorize]
    [ApiController]
    public class AddUserToNodeController : ControllerBase
    {
        private readonly ISave _save;
        private readonly IUser _user;
        private readonly INode _node;
        private readonly IUserFinancial _userFinancial;
        private readonly UserManager<AppUser> _userManager;
        private readonly IFinancialPackage _financialPackage;

        public AddUserToNodeController(
              IUser user
            , ISave save
            , INode node
            , IUserFinancial userFinancial
            , UserManager<AppUser> userManager
            , IFinancialPackage financialPackage)
        {
            _user = user;
            _save = save;
            _node = node;
            _userManager = userManager;
            _userFinancial = userFinancial;
            _financialPackage = financialPackage;
        }

        [HttpPost]
        public async Task<ActionResult> CreateNode(CreateNodeDto createNodeDto)
        {

            //Get parent of curent user by introduction code
            var parentUser = await _user
                .FirstOrDefaultAsync(u => u.IntroductionCode == createNodeDto.IntroductionCode, b => b.Node);

            if (parentUser.Node is null)
                return BadRequest("Introduction code is invalid !");

            var curentUser = await _userManager.Users.Include(u => u.Node)
                .FirstOrDefaultAsync(f => f.Email == User.FindFirstValue(ClaimTypes.Email));

            if (curentUser is null)
                return BadRequest("user not found !");

            if (curentUser.Node is not null)
                if (curentUser.Node.ParentId is not null)
                    return BadRequest("You in set !");

            if (parentUser.Node.LeftUserId is null)
            {
                var res = await CreateNode(isLeft: true, parentUser, curentUser, createNodeDto);

                if (res)
                    return Ok();

                return BadRequest("Something is wrong !");
            }
            else if (parentUser.Node.RightUserId is null)
            {
                var res = await CreateNode(isLeft: false, parentUser, curentUser, createNodeDto);

                if (res)
                    return Ok();

                return BadRequest("Something is wrong !");
            }
            else
            {
                return BadRequest("Introduction code is complete");
            }
        }

        private Node MapNode(AppUser user, AppUser parent, CreateNodeDto createNodeDto) =>
            new Node()
            {
                AppUser = user,
                LeftUserId = null,
                RightUserId = null,
                ParentId = parent.Id,
                TotalMoneyInvested = createNodeDto.UserFinancialDTO.AmountInPackage
            };


        private async Task<bool> CreateNode(
              bool isLeft
            , AppUser parentUser
            , AppUser curentUser
            , CreateNodeDto createNodeDto)
        {

            var curentNode = MapNode(curentUser, parentUser, createNodeDto);

            //this is a helper method for create user financial package . location : Application -> Helpers
            var res = await UserFinancialPackageHelper
                .CreateUserFinancialPackage(
                      curentUser
                    , _userFinancial
                    , createNodeDto.UserFinancialDTO
                    , _financialPackage);

            if (!res)
                return false;

            await _node.CreateAsync(curentNode);

            if (isLeft)
                parentUser.Node.LeftUserId = curentUser.Id;
            else
                parentUser.Node.RightUserId = curentUser.Id;

            await _user.UpdateAsync(parentUser);

            await _userManager.AddToRoleAsync(curentUser, "node");



            var parentNode = parentUser.Node;

            parentNode.TotalMoneyInvestedBySubsets += createNodeDto.UserFinancialDTO.AmountInPackage;

            parentNode.MinimumSubBrachInvested = await MinimumSubBranch(parentNode);

            _node.Update(parentNode);

            do
            {
                if (!curentNode.IsCalculate)
                {
                    //this is the logined user (curent user) parent`s parent !
                    parentNode = await _node
                        .FirstOrDefaultAsync(x => x.AppUser.Id == parentNode.ParentId, y => y.AppUser);

                    //If the current user is an Admin child(in one step); This condition applies
                    if (parentNode is null)
                    {
                        await _save.SaveChangeAsync();

                        return true;
                    }

                    parentNode.TotalMoneyInvestedBySubsets += curentNode.TotalMoneyInvested;

                    parentNode.MinimumSubBrachInvested = await MinimumSubBranch(parentNode);

                    parentNode.AppUser.CommissionPaid = false;

                    _node.Update(parentNode);
                }
                else
                    continue;

            } while (parentNode.ParentId is not null);

            await _save.SaveChangeAsync();

            return true;
        }
        private async Task<decimal> MinimumSubBranch(Node node)
        {

            var leftNode = await _node.FirstOrDefaultAsync(x => x.UserId == node.LeftUserId, y => y.AppUser);
            var rightNode = await _node.FirstOrDefaultAsync(x => x.UserId == node.RightUserId, y => y.AppUser);


            if (rightNode is null)
                return 0;

            if (leftNode.LeftUserId is null && rightNode.LeftUserId is null)
            {
                return
                    leftNode.TotalMoneyInvested > rightNode.TotalMoneyInvested ?
                    rightNode.TotalMoneyInvested : leftNode.TotalMoneyInvested;
            }
            else if (leftNode.LeftUserId is not null && rightNode.LeftUserId is not null &&
                !leftNode.IsCalculate && !rightNode.IsCalculate)
            {
                return
                    leftNode.TotalMoneyInvestedBySubsets + leftNode.TotalMoneyInvested >
                    rightNode.TotalMoneyInvestedBySubsets + rightNode.TotalMoneyInvested ?

                    rightNode.TotalMoneyInvestedBySubsets + rightNode.TotalMoneyInvested
                    : leftNode.TotalMoneyInvestedBySubsets + leftNode.TotalMoneyInvested;
            }
            else if (leftNode.LeftUserId is not null && rightNode.LeftUserId is not null &&
                leftNode.IsCalculate && !rightNode.IsCalculate)
            {
                return
                    leftNode.TotalMoneyInvestedBySubsets >
                    rightNode.TotalMoneyInvestedBySubsets + rightNode.TotalMoneyInvested ?

                    rightNode.TotalMoneyInvestedBySubsets + rightNode.TotalMoneyInvested
                    : leftNode.TotalMoneyInvestedBySubsets;
            }
            else if (leftNode.LeftUserId is not null && rightNode.LeftUserId is not null &&
                !leftNode.IsCalculate && rightNode.IsCalculate)
            {
                return
                    leftNode.TotalMoneyInvestedBySubsets + leftNode.TotalMoneyInvested >
                    rightNode.TotalMoneyInvestedBySubsets ?

                    rightNode.TotalMoneyInvestedBySubsets
                    : leftNode.TotalMoneyInvestedBySubsets + leftNode.TotalMoneyInvested;
            }
            else if (leftNode.LeftUserId is not null && rightNode.LeftUserId is not null &&
                leftNode.IsCalculate && rightNode.IsCalculate)
            {
                return
                    leftNode.TotalMoneyInvestedBySubsets > rightNode.TotalMoneyInvestedBySubsets ?
                    rightNode.TotalMoneyInvestedBySubsets : leftNode.TotalMoneyInvestedBySubsets;
            }
            else if (leftNode.LeftUserId is not null || rightNode.LeftUserId is not null)
            {
                return
                    leftNode.TotalMoneyInvestedBySubsets > rightNode.TotalMoneyInvestedBySubsets ?
                    rightNode.TotalMoneyInvestedBySubsets : leftNode.TotalMoneyInvestedBySubsets;
            }

            else
                return 0;
        }
    }
}
