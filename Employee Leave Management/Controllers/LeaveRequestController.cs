using AutoMapper;
using LeaveManagement.Contracts;
using LeaveManagement.Data;
using LeaveManagement.Data.Migrations;
using LeaveManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LeaveManagement.Email;

namespace LeaveManagement.Controllers
{
    [Authorize]
    public class LeaveRequestController : Controller
    {
        private readonly ILeaveRequestRepository _leaveRequestRepo;
        private readonly ILeaveTypeRepository _leaveTypeRepo;
        private readonly ILeaveAllocationRepository _leaveAllocRepo;
        private readonly IMapper _mapper;
        private readonly UserManager<Employee> _userManager;
        private readonly IEmailSender _emailSender;

        public LeaveRequestController(
            ILeaveRequestRepository leaveRequestRepo,
            ILeaveTypeRepository leaveTypeRepo,
            ILeaveAllocationRepository leaveAllocRepo,
            IMapper mapper,
            UserManager<Employee> userManager,
            IEmailSender emailSender
        )
        {
            _leaveRequestRepo = leaveRequestRepo;
            _leaveTypeRepo = leaveTypeRepo;
            _leaveAllocRepo = leaveAllocRepo;
            _mapper = mapper;
            _userManager = userManager;
            _emailSender = emailSender;
        }

        [Authorize(Roles ="Administrator, Manager")]
        // GET: LeaveRequestController
        public ActionResult Index()
        {
            var employee = _userManager.GetUserAsync(User).Result;

            var leaveRequests = _leaveRequestRepo.FindAll();
            var leaveRequestsModel = _mapper.Map<List<LeaveRequestVM>>(leaveRequests);
            if (employee.Role == "Manager")
            {
                leaveRequestsModel = leaveRequestsModel.Where(o => o.RequestingManagerId == employee.Id).ToList();
            }

            var model = new AdminLeaveRequestViewVM
            {
                TotalRequests = leaveRequestsModel.Count,
                ApprovedRequests = leaveRequestsModel.Count(q => q.Approved == true),
                PendingRequests = leaveRequestsModel.Count(q => q.Approved == null),
                RejectedRequests = leaveRequestsModel.Count(q => q.Approved == false),
                LeaveRequests = leaveRequestsModel
            };
            return View(model);
        }

        public ActionResult MyLeave()
        {
            var employee = _userManager.GetUserAsync(User).Result;
            var employeeId = employee.Id;
            var employeeAllocations = _leaveAllocRepo.GetLeaveAllocationsByEmployee(employeeId);
            var employeeRequests = _leaveRequestRepo.GetLeaveRequestsByEmployee(employeeId);

            var employeeAllocationsModel = _mapper.Map<List<LeaveAllocationVM>>(employeeAllocations);
            var employeeRequestsModel = _mapper.Map<List<LeaveRequestVM>>(employeeRequests);

            var model = new EmployeeLeaveRequestViewVM
            {
                LeaveAllocations = employeeAllocationsModel,
                LeaveRequests = employeeRequestsModel
            };

            return View(model);
        } 

        // GET: LeaveRequestController/Details/5
        public ActionResult Details(int id)
        {
            var leaveRequest = _leaveRequestRepo.FindById(id);
            var model = _mapper.Map<LeaveRequestVM>(leaveRequest);
            return View(model);
        }

        public ActionResult ApproveRequest(int id)
        {
            try
            {
                var user = _userManager.GetUserAsync(User).Result;
                var leaveRequest = _leaveRequestRepo.FindById(id);
                var employeeid = leaveRequest.RequestingEmployeeId;
                var leaveTypeid = leaveRequest.LeaveTypeId;
                var allocation = _leaveAllocRepo.GetLeaveAllocationsByEmployeeAndType(employeeid,leaveTypeid);
                double daysRequested = (leaveRequest.EndDate - leaveRequest.StartDate).TotalHours / 8;
                allocation.NumberOfDays = allocation.NumberOfDays - daysRequested;
                
                leaveRequest.Approved = true;
                leaveRequest.ApprovedById = user.Id;
                leaveRequest.DateActioned = DateTime.Now;

                _leaveRequestRepo.Update(leaveRequest);
                _leaveAllocRepo.Update(allocation);

                string content = string.Format("{0} has approved your leave on {1} to {2}",
                        user.Firstname + " " + user.Lastname, 
                        leaveRequest.StartDate.ToString("dd/MM/yyyy HH:mm"),
                        leaveRequest.EndDate.ToString("dd/MM/yyyy HH:mm"));

                var message = new Message(new List<EmailDetails> { new EmailDetails { Name = leaveRequest.RequestingEmployee.Firstname + " " + leaveRequest.RequestingEmployee.Lastname, Email = leaveRequest.RequestingEmployee.Email } }, "Leave Approved", content);
                _emailSender.SendEmail(message);

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                return RedirectToAction(nameof(Index));
            }
            
        }

        public ActionResult RejectRequest(int id)
        {
            try
            {
                var user = _userManager.GetUserAsync(User).Result;
                var leaveRequest = _leaveRequestRepo.FindById(id);
                leaveRequest.Approved = false;
                leaveRequest.ApprovedById = user.Id;
                leaveRequest.DateActioned = DateTime.Now;

                _leaveRequestRepo.Update(leaveRequest);

                string content = string.Format("{0} has rejected your leave on {1} to {2}",
                        user.Firstname + " " + user.Lastname,
                        leaveRequest.StartDate.ToString("dd/MM/yyyy HH:mm"),
                        leaveRequest.EndDate.ToString("dd/MM/yyyy HH:mm"));

                var message = new Message(new List<EmailDetails> { new EmailDetails { Name = leaveRequest.RequestingEmployee.Firstname + " " + leaveRequest.RequestingEmployee.Lastname, Email = leaveRequest.RequestingEmployee.Email } }, "Leave Rejected", content);
                _emailSender.SendEmail(message);

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                return RedirectToAction(nameof(Index));
            }

        }

        // GET: LeaveRequestController/Create
        public ActionResult Create()
        {
            var employee = _userManager.GetUserAsync(User).Result;

            var leaveTypes = _leaveTypeRepo.FindAll();
            var leaveTypeItems = leaveTypes.Select(q => new SelectListItem { 
                Text = q.Name,
                Value = q.Id.ToString()
            });

            var managerList = _leaveRequestRepo.GetManagerList();
            var managerListItems = managerList.Select(q => new SelectListItem
            {
                Text = q.Firstname + " " + q.Lastname,
                Value = q.Id
            });

            var model = new CreateLeaveRequestVM
            {
                EmployeeName = employee.Firstname + " " + employee.Lastname,
                LeaveTypes = leaveTypeItems,
                ManagerList = managerListItems
            };
            return View(model);
        }

        // POST: LeaveRequestController/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create(CreateLeaveRequestVM model)
        {
            try
            {
                var startDate = DateTime.ParseExact(model.StartDate, "dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                var endDate = DateTime.ParseExact(model.EndDate, "dd/MM/yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                var leaveTypes = _leaveTypeRepo.FindAll();
                var leaveTypeItems = leaveTypes.Select(q => new SelectListItem
                {
                    Text = q.Name,
                    Value = q.Id.ToString()
                });
                model.LeaveTypes = leaveTypeItems;
                if (!ModelState.IsValid)
                {
                    return View(model);
                }

                if (DateTime.Compare(startDate, endDate) > 1)
                {
                    ModelState.AddModelError("", "Start date cannot be further in the future than the end date");
                    return View(model);
                }

                var employee = _userManager.GetUserAsync(User).Result;
                var allocation = _leaveAllocRepo.GetLeaveAllocationsByEmployeeAndType(employee.Id, model.LeaveTypeId);
                var daysRequested = (endDate - startDate).TotalHours / 8 ; // divide by work hours, default is 8 (hardcoded) 

                if (daysRequested > allocation.NumberOfDays) 
                {
                    ModelState.AddModelError("", "You do not have sufficient days for this request!");
                    return View(model);
                }

                var leaveRequestModel = new LeaveRequestVM
                {
                    RequestingEmployeeId = employee.Id,
                    StartDate = startDate,
                    EndDate = endDate,
                    Approved = null,
                    DateRequested = DateTime.Now,
                    DateActioned = DateTime.Now,
                    LeaveTypeId = model.LeaveTypeId,
                    RequestComments = model.RequestComments,
                    RequestingManagerId = model.Manager
                };

                var leaveRequest = _mapper.Map<LeaveRequest>(leaveRequestModel);
                var isSuccess = _leaveRequestRepo.Create(leaveRequest);

                if (!isSuccess)
                {
                    ModelState.AddModelError("", "Something went wrong with submitting your record");
                    return View(model);
                }
                else
                {
                    var manager = _userManager.FindByIdAsync(leaveRequest.RequestingManagerId).Result;

                    string content = string.Format("{0} has apply leave on {1} to {2}", 
                        employee.Firstname + " " + employee.Lastname, startDate, endDate);

                    var message = new Message(new List<EmailDetails> { new EmailDetails { Name = manager.Firstname + " " + manager.Lastname, Email = manager.Email } }, "Leave Application", content);
                    _emailSender.SendEmail(message);
                }

                return RedirectToAction("MyLeave");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Something went wrong");
                return View(model);
            }
        }

        public ActionResult CancelRequest(int id)
        {
            var leaveRequest = _leaveRequestRepo.FindById(id);
            leaveRequest.Cancelled = true;
            _leaveRequestRepo.Update(leaveRequest);
            return RedirectToAction("MyLeave");
        }

        // GET: LeaveRequestController/Edit/5
        public ActionResult Edit()
        {
            return View();
        }

        // POST: LeaveRequestController/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }

        // GET: LeaveRequestController/Delete/5
        public ActionResult Delete()
        {
            return View();
        }

        // POST: LeaveRequestController/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Delete(int id, IFormCollection collection)
        {
            try
            {
                return RedirectToAction(nameof(Index));
            }
            catch
            {
                return View();
            }
        }
    }
}
