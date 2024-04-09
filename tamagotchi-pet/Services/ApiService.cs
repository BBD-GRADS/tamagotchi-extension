﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using tamagotchi_pet.Models;
using System.Diagnostics;
using Microsoft.Build.Framework;
using tamagotchi_pet.Utils;

namespace tamagotchi_pet.Services
{
    public class ApiService
    {
        private static readonly HttpClient client = new HttpClient();

        static ApiService()
        {
            client.BaseAddress = new Uri("https://localhost:32794/"); //change env var TODO
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public static async Task<(bool success, string message)> AuthenticateAsync(string idToken)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
            try
            {
                HttpResponseMessage response = await client.GetAsync("api/User/Auth");
                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    return (true, "Logged in successfully!");
                }
                else
                {
                    return (false, "Authentication failed: " + response.ReasonPhrase);
                }
            }
            catch (HttpRequestException e)
            {
                return (false, "Network error: " + e.Message);
            }
        }

        public static async Task<Pet> GetPetAsync(string idToken)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", idToken);
            try
            {
                HttpResponseMessage response = await client.GetAsync("api/Pet");
                if (response.IsSuccessStatusCode)
                {
                    var pet = JsonConvert.DeserializeObject<Pet>(await response.Content.ReadAsStringAsync());
                    return pet;
                }
            }
            catch (HttpRequestException e)
            {
                Logging.Logger.Debug($"Network error: {e.Message}");
            }
            return null;
        }

        public static async Task<Pet> CreatePetAsync(string idToken, string petName)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", idToken);

            var petDTO = new
            {
                PetName = petName
            };
            var jsonContent = JsonConvert.SerializeObject(petDTO);
            var contentString = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try
            {
                HttpResponseMessage response = await client.PostAsync("api/Pet", contentString);
                if (response.IsSuccessStatusCode)
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    Pet createdPet = JsonConvert.DeserializeObject<Pet>(responseBody);
                    return createdPet;
                }
            }
            catch (HttpRequestException e)
            {
                Logging.Logger.Debug($"Network error: {e.Message}");
            }
            return null;
        }
    }
}