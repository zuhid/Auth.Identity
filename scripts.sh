#!/bin/bash

################################################## variables ##################################################
postgres_image="postgis/postgis:16-3.5"
postgres_container="postgres"
postgres_user="postgres"
postgres_password="P@ssw0rd"
dbuser_user="dbuser"
dbuser_password="P@ssw0rd"

################################################## functions ##################################################

rebuild_postgres_server(){
  # remove the image
  docker container rm "$postgres_container" --force
  # Run the PostgreSQL container
  docker run --name $postgres_container --publish 5432:5432 --detach \
    --env "POSTGRES_USER=$postgres_user" \
    --env "POSTGRES_PASSWORD=$postgres_password" \
    "$postgres_image"
  # sleep for a few seconds to let the docker service start up
  sleep 5
  # create user 'dbuser'
  docker exec -it $postgres_container psql -U $postgres_user -c "create user dbuser with superuser password 'P@ssw0rd';"
}

# This script updates all outdated NuGet packages in .NET projects within the current directory.
update_dotnet_packages() {
  find . -name "*.csproj" | sort | while read csproj; do
    echo "Processing project: $csproj"
    dotnet list "$csproj" package --outdated | grep -E '>' | awk '{print $2, $4}' |
    while read name version; do
      echo "Updating package: $name to project: $csproj"
      dotnet add "$csproj" package "$name"
    done
  done
  # dotnet list package --outdated
  dotnet tool update --all # Update all tools
}

solution_initilize(){
  project=$1
  dotnet tool restore
}

recreate_database(){
  # get the database in lower case
  dbname=${1,,}
  # kill all connection
  docker exec -it $postgres_container psql -U $postgres_user -c "
  revoke connect on database $dbname from public;
  select pg_terminate_backend(pid)
  from pg_stat_activity
  where datname = '$dbname'
    and pid <> pg_backend_pid();
  "
  # drop the database
  docker exec -it $postgres_container psql -U $postgres_user -c "drop database if exists $dbname;"
  # create the database
  docker exec -it $postgres_container psql -U $postgres_user -c "create database $dbname;"
  # create the __EFMigrationsHistory table
  docker exec -it $postgres_container psql -U $postgres_user -d $dbname -c '
  create table if not exists "__EFMigrationsHistory" (
    "MigrationId" character varying(150) not null,
    "PostgresTypeVersion" character varying(32) not null,
    constraint "PK___EFMigrationsHistory" primary key ("MigrationId")
  );'
}

update_database(){
  project=$1
  startup_project=$2
  rm -rf $project/Migrations # remove migration folder
  # dotnet build
  dotnet ef migrations add Initial --project $project --startup-project $startup_project # create initial migration
  dotnet ef migrations script --project $project --startup-project $startup_project --output $project/Migrations/Script.sql # create script
  dotnet ef database drop --project $project --startup-project $startup_project --force # drop database
  dotnet ef database update --project $project --startup-project $startup_project # update database
}

run_test() {
  project=$1
  rm -rf $project/TestResults
  dotnet test $project --settings $project/coverlet.runsettings --collect:"XPlat Code Coverage"
  dotnet reportgenerator -reports:"$project/TestResults/*/coverage.cobertura.xml" -targetdir:"$project/TestResults/CoverageReport" -reporttypes:Html
  echo "$PWD/$project/TestResults/CoverageReport/index.html"

  firefox "$PWD/$project/TestResults/CoverageReport/index.html" &
  # google-chrome "$PWD/$project/TestResults/CoverageReport/index.html" &
  # /opt/microsoft/msedge/msedge "$PWD/$project/TestResults/CoverageReport/index.html" &

  # "/c/Program Files/Mozilla Firefox/firefox.exe" "$PWD/$project/TestResults/CoverageReport/index.html" &
  # "/c/Program Files/Google/Chrome/Application/chrome.exe" "$PWD/$project/TestResults/CoverageReport/index.html" &
}

generate_rsa_keys() {
  output_dir="${1:-.}/Keys"
  openssl genrsa -out "$output_dir/private.key" 2048
  openssl rsa -in "$output_dir/private.key" -pubout -out "$output_dir/public.key"
}

publish_docker() {
  file=$1
  tag=$2
  docker build --tag tzather/$tag --file $file .
  docker push tzather/$tag # push container https://hub.docker.com/u/tzather
}

################################################## execute ##################################################
clear
# docker start $postgres_container
# docker start mailpit
# rebuild_postgres_server
# update_dotnet_packages
# solution_initilize

# recreate_database "identity_csharp"
# update_database "Auth" "Auth"
run_test "Auth.Tests"
# publish_docker Auth/Dockerfile Auth
