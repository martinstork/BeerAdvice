&#x1F534; AZURE SERVICE (STUDENT PLAN) HAS BEEN DISABLED. DEPLOY ON OWN ENVIRONMENT TO TEST THE API.

 ---

# BeerAdvice

Build status:  
[![Build Status](https://dev.azure.com/560831/BeerAdvice/_apis/build/status/martinstork.BeerAdvice?branchName=master)](https://dev.azure.com/560831/BeerAdvice/_build/latest?definitionId=3&branchName=master)

Release status:  
![Deployment Status](https://vsrm.dev.azure.com/560831/_apis/public/Release/badge/44509042-753a-44e0-b515-2a00e8989605/2/2)

## API for getting advice whether to drink beer or Jägertee

The function can be called with **http://beeradvice.azurewebsites.net/api/BeerAdviceFunction**  
followed by a city [**?city=muiden**](http://beeradvice.azurewebsites.net/api/BeerAdviceFunction?city=muiden).

You will retrieve a URL to your advice image. The URL is created before the image is available. When there's no image just call the URL again (it takes a couple of seconds).


### Example results

![Get Beer advice](https://i.imgur.com/zntmPLj.png)

![Get Jägertee advice](https://i.imgur.com/RIkD11W.png)


## Deployment

### Prerequisites
The API makes use of 2 other API's. [**Open Weather**](https://openweathermap.org) and [**Azure Maps**]("").

You need to register an [**Open Weather**](https://openweathermap.org) account and create a key to use the **Current weather data** service.


### Create needed resources
Create / update the required resources using the Azure ARM template [**BeerAdvice/Deployment/generated-template/template.json**](https://github.com/martinstork/BeerAdvice/blob/master/BeerAdvice/Deployment/generated-template/template.json). 


#### Information you NEED to change
`sites_api_name` : Name of the API (used in url).

`serverfarm_name` : Name of the server farm.


#### Information you CAN change
`accounts_microsoft_maps_name` : Name of Microsoft Maps resource.

`storageAccounts_apistorage_name` : Name of the Storage resource.

If needed you can adjust the resource locations, tiers and application-insights etc.

### Deploy Azure functions app
<details><summary><b>Deploy with Visual Studio</b></summary>
Sign in to the Microsoft account on which the resources are created and simply select publish to Azure.

Select an Azure App Service and you're done.

![Screenshot](https://i.imgur.com/rxiijEs.png)
</details>

<details><summary><b>Deployment using CI/CD</b></summary>
This repository is configured with a build and release pipeline. Every time the master branch gets updated a build will automatically follow.
If this build succeeds a release will follow (README.md changes are ignored).

**Build pipeline:**
[BeerAdvice/azure-pipelines](https://github.com/martinstork/BeerAdvice/blob/master/azure-pipelines.yml)

**Release pipeline:**

![Screenshot](https://i.imgur.com/3q8i8dq.png)

</details>

### Environment variables
The release pipeline for this repository is configured to set the environment variables automatically.
To make the API work in a different environment you need to setup the following environment variables:

| Variable | Description | Value |
| --- | --- | --- |
| `OpenWeatherKey` | Your own [Open Weather](https://openweathermap.org) key|
| `MapsKey` | Key of your created Microsoft Maps resource |
| `StorageName` | Name of your created Storage resource |
| `StorageKey` | Key of your created Storage resource | 
| `ContainerReference` | Reference to the map blob container | *mapblobs* |

The container reference value should be *mapblobs*.
