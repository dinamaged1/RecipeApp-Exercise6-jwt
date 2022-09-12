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
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey
            (Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])),
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = false,
        ValidateIssuerSigningKey = true
    };
});
builder.Services.AddAuthorization();

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseCors();

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
    if (newUser.UserPassword.IsNullOrEmpty() || newUser.UserPassword.Length<6)
    {
        return Results.BadRequest("Invalid password!");
    }
    else if(newUser.UserName.IsNullOrEmpty() || usersList.Exists(x => x.UserName == newUser.UserName))
    {
        return Results.BadRequest("Invalid user name");
    }

    CreatePasswordHash(newUser.UserPassword, out byte[] passwordHash, out byte[] passwordSalt);
    User addedUser = new User();
    addedUser.UserName = newUser.UserName;
    addedUser.PasswordHash = passwordHash;
    addedUser.PasswordSalt = passwordSalt;
    
    usersList.Add(addedUser);
    await SaveUserToJson();
    var stringToken = CreateToken(newUser.UserName);
    return Results.Ok(stringToken);
});

//Get all recipes
app.MapGet("/recipes", () =>
{
    if (recipesList != null)
        return Results.Ok(recipesList);
    else
        return Results.NoContent();
}).WithName("GetRecipes");

//Get specific  recipe
app.MapGet("/recipe/{id}", (Guid id) =>
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
app.MapPost("/recipe", async ([FromBody] Recipe newRecipe) =>
{
    recipesList.Add(newRecipe);
    await SaveRecipeToJson();
    return Results.Ok(recipesList);
});

//Edit recipe
app.MapPut("/recipe/{id}", async (Guid id, [FromBody] Recipe newRecipeData) =>
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
app.MapDelete("/recipe/{id}", async (Guid id) =>
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
app.MapGet("/categories", () =>
{
    if (categoryList != null)
        return Results.Ok(categoryList);
    else
        return Results.NoContent();
});

//Add category
app.MapPost("/category", async ([FromBody] string newCategory) =>
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
app.MapPut("/category/{name}", async (string name, [FromBody] string newCategoryName) =>
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
app.MapDelete("category/{name}", async (string name) =>
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
        Expires = DateTime.UtcNow.AddMinutes(5),
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

void CreatePasswordHash(string password, out byte[] passwordHash, out byte[] passwordSalt)
{
    using (var hmac= new HMACSHA512())
    {
        passwordSalt = hmac.Key;
        passwordHash = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
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
