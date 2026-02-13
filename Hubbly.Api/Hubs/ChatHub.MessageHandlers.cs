using Hubbly.Domain.Dtos;
using Microsoft.AspNetCore.SignalR;

namespace Hubbly.Api.Hubs;

public partial class ChatHub
{
	#region Обработчики сообщений

	public async Task SendMessage(string content, string? actionType = null, long? timestamp = null, string? nonce = null)
	{
		var userId = Guid.Parse(Context.User!.FindFirst("userId")!.Value);

		using (_logger.BeginScope(new Dictionary<string, object>
		{
			["UserId"] = userId,
			["ContentLength"] = content?.Length ?? 0
		}))
		{
			_logger.LogDebug("SendMessage called");

			// Валидация
			if (!timestamp.HasValue)
			{
				_logger.LogWarning("Missing timestamp");
				await Clients.Caller.SendAsync("ReceiveError", "Missing timestamp");
				return;
			}

			if (string.IsNullOrEmpty(nonce) || !IsNonceValid(nonce, timestamp.Value))
			{
				_logger.LogWarning("Invalid message token");
				await Clients.Caller.SendAsync("ReceiveError", "Invalid message token");
				return;
			}

			if (content?.Length > 500)
			{
				_logger.LogWarning("Message too long: {Length}", content.Length);
				await Clients.Caller.SendAsync("ReceiveError", "Message too long");
				return;
			}

			try
			{
				var room = await _roomService.GetRoomByUserIdAsync(userId);
				if (room == null)
				{
					_logger.LogWarning("User not in a room");
					await Clients.Caller.SendAsync("ReceiveError", "You are not in a room");
					return;
				}

				var messageDto = await _chatService.SendMessageAsync(userId, content, actionType);
				await Clients.Group(room.Id.ToString()).SendAsync("ReceiveMessage", messageDto);

				_logger.LogInformation("Message sent by {Sender} in {RoomName}",
					messageDto.SenderNickname, room.Name);
			}
			catch (KeyNotFoundException ex)
			{
				_logger.LogError(ex, "User not found");
				await Clients.Caller.SendAsync("ReceiveError", "User not found");
			}
			catch (InvalidOperationException ex)
			{
				_logger.LogError(ex, "Invalid message content");
				await Clients.Caller.SendAsync("ReceiveError", ex.Message);
			}
			catch (Exception ex)
			{
				_logger.LogError(ex, "SendMessage error for user {UserId}", userId);
				await Clients.Caller.SendAsync("ReceiveError", "Failed to send message");
			}
		}
	}

	public async Task UserTyping()
	{
		var userId = Guid.Parse(Context.User!.FindFirst("userId")!.Value);

		try
		{
			var room = await _roomService.GetRoomByUserIdAsync(userId);
			if (room == null)
			{
				await Clients.Caller.SendAsync("ReceiveError", "You are not in a room");
				return;
			}

			var user = await _userService.GetUserProfileAsync(userId);
			await Clients.OthersInGroup(room.Id.ToString()).SendAsync("UserTyping", new UserTypingData
			{
				UserId = userId.ToString(),
				Nickname = user.Nickname
			});

			_logger.LogTrace("User {Nickname} is typing", user.Nickname);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "UserTyping error for user {UserId}", userId);
		}
	}

	public async Task SendAnimation(string animationType)
	{
		var userId = Guid.Parse(Context.User!.FindFirst("userId")!.Value);

		try
		{
			var room = await _roomService.GetRoomByUserIdAsync(userId);
			if (room == null)
			{
				await Clients.Caller.SendAsync("ReceiveError", "You are not in a room");
				return;
			}

			await Clients.OthersInGroup(room.Id.ToString()).SendAsync("UserPlayAnimation",
				userId.ToString(), animationType);

			await Clients.Caller.SendAsync("UserPlayAnimation",
				userId.ToString(), animationType);

			_logger.LogInformation("Animation {Animation} sent by user {UserId}", animationType, userId);
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "SendAnimation error for user {UserId}", userId);
			await Clients.Caller.SendAsync("ReceiveError", "Failed to send animation");
		}
	}

	public Task<int> GetOnlineCount()
	{
		var count = _connectedUsers.Count;
		_logger.LogDebug("Online count requested: {Count}", count);
		return Task.FromResult(count);
	}

	#endregion
}