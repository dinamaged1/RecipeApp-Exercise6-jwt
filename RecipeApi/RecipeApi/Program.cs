using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using RecipeApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.Filters;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(o =>
{
    o.TokenValidationParameters = new TokenValidationParameters
    {
        IssuerSigningKey = new SymmetricSecurityKey
            (Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])),
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.Zero
    };
});
builder.Services.AddAuthorization();

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
    {
        Description = "Standard Authorization header using the Bearer scheme (\"bearer {token}\")",
        In = ParameterLocation.Header,
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey
    });

    options.OperationFilter<SecurityRequirementsOperationFilter>();
});

var app = builder.Build();

app.UseCors(x => x
    .AllowAnyMethod()
    .AllowAnyHeader()
    .SetIsOriginAllowed(origin => true) // allow any origin
    .AllowCredentials()); // allow credentials

app.UseAuthentication();
app.UseAuthorization();

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "v1");
    options.RoutePrefix = string.Empty;
});

app.UseHttpsRedirection();

//Desrialize recipe file and category file
List<User>? savedUsers = new();
List<Recipe>? savedRecipes = new();
List<string>? savedCategories = new();
try
{
    string userJson = await ReadJsonFile("user");
    string recipeJson = await ReadJsonFile("recipe");
    string categoryJson = await ReadJsonFile("category");

    savedRecipes = JsonSerializer.Deserialize<List<Recipe>>(recipeJson);
    savedCategories = JsonSerializer.Deserialize<List<string>>(categoryJson);
    savedUsers = JsonSerializer.Deserialize<List<User>>(userJson);
}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
    return;
}

//Create list of recipes and list of categories
List<User> usersList = new List<User>(savedUsers!);
List<Recipe> recipesList = new List<Recipe>(savedRecipes!);
List<string> categoryList = new List<string>(savedCategories!);

// User register
app.MapPost("/register", async ([FromBody] UserDto newUser) =>
{
    if (newUser.UserPassword.IsNullOrEmpty())
    {
        return Results.BadRequest("Invalid password!");
    }
    else if (newUser.Username.IsNullOrEmpty() || usersList.Exists(x => x.Username == newUser.Username))
    {
        return Results.BadRequest("Invalid user name");
    }

    CreatePasswordHash(newUser.UserPassword, out byte[] passwordHash, out byte[] passwordSalt);
    User addedUser = new User();
    addedUser.Id = new Guid().ToString();
    addedUser.Username = newUser.Username;
    addedUser.PasswordHash = passwordHash;
    addedUser.PasswordSalt = passwordSalt;

    usersList.Add(addedUser);
    await SaveUserToJson();
    var stringToken = CreateToken(newUser.Username);
    return Results.Ok(stringToken);
});

// User login
app.MapPost("/login",async ([FromBody] UserDto enteredUser, Microsoft.AspNetCore.Http.HttpResponse response) =>
{

    if (!usersList.Exists(x => x.Username == enteredUser.Username))
    {
        Results.Text("User not found");
        return Results.NotFound("User not found");
    }
    else if (enteredUser.Username.IsNullOrEmpty())
    {
        return Results.BadRequest("you entered empty user name!");
    }
    User? userData = usersList.FirstOrDefault((x) => x.Username == enteredUser.Username);
    if (!VerifyPassword(enteredUser.UserPassword, userData.PasswordHash, userData.PasswordSalt))
    {
        return Results.BadRequest("Password incorrect");
    }

    var token = CreateToken(enteredUser.Username);

    var refreshToken = GenerateRefreshToken();
    SetRefreshToken(refreshToken, token, userData, response);

    userData.RefreshToken = refreshToken.Token.ToString();
    await SaveUserToJson();

    List<string> tokens = new List<string>()
    {
        token,
        refreshToken.Token.ToString()
    };
    return Results.Ok(tokens);
});

// Refresh token
app.MapPost("/refreshToken", async ([FromBody] string username, Microsoft.AspNetCore.Http.HttpRequest request, Microsoft.AspNetCore.Http.HttpResponse response) =>
{
    User? specifiedUser = new User();
    var refreshToken = request.Cookies["refreshToken"];
    if (!usersList.IsNullOrEmpty() && !username.IsNullOrEmpty() && usersList.Exists(x => x.Username == username))
    {
        specifiedUser = usersList.Find(x => x.Username == username);
    }
    else
    {
        return Results.BadRequest();
    }
    if (!specifiedUser.RefreshToken.Equals(refreshToken))
    {
        return Results.Json("Invalid Refresh Token.", statusCode: 401);
    }
    else if (specifiedUser.TokenExpires < DateTime.Now)
    {
        return Results.Json("Token Expired.", statusCode: 401);
    }
    string token = CreateToken(username);
    var newRefreshToken = GenerateRefreshToken();
    SetRefreshToken(newRefreshToken, token, specifiedUser, response);

    specifiedUser.RefreshToken = newRefreshToken.Token.ToString();
    await SaveUserToJson();

    List<string> tokens = new List<string>()
    {
        token,
        newRefreshToken.Token.ToString()
    };
    return Results.Ok(tokens);

});

//Get all recipes
app.MapGet("/recipes", [Authorize] () =>
{
    if (recipesList != null)
        return Results.Ok(recipesList);
    else
        return Results.NoContent();
}).WithName("GetRecipes");

//Get specific  recipe
app.MapGet("/recipe/{id}", [Authorize] (Guid id) =>
{
    var selectedRecipeIndex = recipesList.FindIndex(x => x.Id == id);
    if (selectedRecipeIndex != -1)
    {
        return Results.Ok(recipesList[selectedRecipeIndex]);
    }
    else
    {
        return Results.NotFound();
    }
});

//Add new recipe
app.MapPost("/recipe", [Authorize] async ([FromBody] Recipe newRecipe) =>
{
    recipesList.Add(newRecipe);
    await SaveRecipeToJson();
    return Results.Ok(recipesList);
});

//Edit recipe
app.MapPut("/recipe/{id}", [Authorize] async (Guid id, [FromBody] Recipe newRecipeData) =>
{
    var selectedRecipeIndex = recipesList.FindIndex(x => x.Id == id);
    if (selectedRecipeIndex != -1)
    {
        recipesList[selectedRecipeIndex] = newRecipeData;
        await SaveRecipeToJson();
        return Results.Ok(recipesList);
    }
    else
    {
        return Results.NotFound();
    }
});

//Remove recipe
app.MapDelete("/recipe/{id}", [Authorize] async (Guid id) =>
{
    var selectedRecipeIndex = recipesList.FindIndex(x => x.Id == id);
    if (selectedRecipeIndex != -1)
    {
        recipesList.Remove(recipesList[selectedRecipeIndex]);
        await SaveRecipeToJson();
        return Results.Ok();
    }
    else
    {
        return Results.NotFound();
    }
});

//Get all categories
app.MapGet("/categories", [Authorize] () =>
{
    if (categoryList != null)
        return Results.Ok(categoryList);
    else
        return Results.NoContent();
}).RequireAuthorization();

//Add category
app.MapPost("/category", [Authorize] async ([FromBody] string newCategory) =>
{
    if (!categoryList.Contains(newCategory) && newCategory != "")
    {
        categoryList.Add(newCategory);
        await SaveCategoryToJson();
        return Results.Ok(categoryList);
    }
    else
    {
        return Results.BadRequest();
    }
});

//Edit category
app.MapPut("/category/{name}", [Authorize] async (string name, [FromBody] string newCategoryName) =>
{
    int indexOfCategory = categoryList.FindIndex(x => x == name);
    if (indexOfCategory != -1 && !categoryList.Contains(newCategoryName) && newCategoryName != "")
    {
        categoryList[indexOfCategory] = newCategoryName;

        //Edit the category name for each recipe belog to this category
        for (int i = 0; i < recipesList.Count; i++)
        {
            for (int j = 0; j < recipesList[i].Categories.Count; j++)
            {
                if (recipesList[i].Categories[j] == name)
                {
                    recipesList[i].Categories[j] = newCategoryName;
                }
            }
        }
        await SaveCategoryToJson();
        await SaveRecipeToJson();
        return Results.Ok(categoryList);
    }
    else
    {
        return Results.BadRequest();
    }
});

//Delete Category
app.MapDelete("category/{name}", [Authorize] async (string name) =>
{
    if (categoryList.Contains(name))
    {
        categoryList.Remove(name);

        //Remove this category from each recipe
        for (int i = 0; i < recipesList.Count; i++)
        {
            for (int j = 0; j < recipesList[i].Categories.Count; j++)
            {
                if (recipesList[i].Categories[j] == name)
                {
                    recipesList[i].Categories.Remove(recipesList[i].Categories[j]);
                }
            }
        }
        await SaveCategoryToJson();
        await SaveRecipeToJson();
        return Results.Ok(categoryList);
    }
    else
    {
        return Results.NotFound();
    }
});

app.Run();

string CreateToken(string userName)
{
    var key = Encoding.ASCII.GetBytes
        (builder.Configuration["Jwt:Key"]);
    var tokenDescriptor = new SecurityTokenDescriptor
    {
        Subject = new ClaimsIdentity(new[]
        {
                new Claim("Id", Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Sub, userName),
                new Claim(JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString())
             }),
        Expires = DateTime.UtcNow.AddMinutes(30),
        SigningCredentials = new SigningCredentials
        (new SymmetricSecurityKey(key),
        SecurityAlgorithms.HmacSha512Signature)
    };
    var tokenHandler = new JwtSecurityTokenHandler();
    var token = tokenHandler.CreateToken(tokenDescriptor);
    var jwtToken = tokenHandler.WriteToken(token);
    var stringToken = tokenHandler.WriteToken(token);
    return stringToken;
}

RefreshToken GenerateRefreshToken()
{
    var refreshToken = new RefreshToken
    {
        Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
        Expires = DateTime.Now.AddDays(7),
        Created = DateTime.Now
    };
    return refreshToken;
}

void SetRefreshToken(RefreshToken newRefreshToken, string accessToken, User loggedUser, Microsoft.AspNetCore.Http.HttpResponse response)
{
    var cookieOptions = new CookieOptions
    {
        HttpOnly = true,
        Expires = newRefreshToken.Expires
    };

    response.Cookies.Append("refreshToken", newRefreshToken.Token, cookieOptions);
    response.Cookies.Append("accessToken", accessToken, cookieOptions);
    loggedUser.RefreshToken = newRefreshToken.Token;
    loggedUser.TokenCreated = newRefreshToken.Created;
    loggedUser.TokenExpires = newRefreshToken.Expires;
}

void CreatePasswordHash(string password, out byte[] passwordHash, out byte[] passwordSalt)
{
    using (var hmac = new HMACSHA512())
    {
        passwordSalt = hmac.Key;
        passwordHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
    }
}

bool VerifyPassword(string password, byte[] passwordHash, byte[] passwordSalt)
{
    using (var hmac = new HMACSHA512(passwordSalt))
    {
        var computedHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
        return computedHash.SequenceEqual(passwordHash);
    }
}

async Task<string> ReadJsonFile(string fileName) =>
await File.ReadAllTextAsync($"{fileName}.json");

async Task WriteJsonFile(string fileName, string fileData) =>
await File.WriteAllTextAsync($"{fileName}.json", fileData);

async Task SaveUserToJson()
{
    string jsonString = JsonSerializer.Serialize(usersList);
    await WriteJsonFile("user", jsonString);
}

async Task SaveRecipeToJson()
{
    string jsonString = JsonSerializer.Serialize(recipesList);
    await WriteJsonFile("recipe", jsonString);
}

async Task SaveCategoryToJson()
{
    string jsonString = JsonSerializer.Serialize(categoryList);
    await WriteJsonFile("category", jsonString);
}
