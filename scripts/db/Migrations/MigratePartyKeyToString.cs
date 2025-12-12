using MySqlConnector;
using Dapper;

namespace DiscordBot.scripts.db.Migrations;

/// <summary>
/// PARTY_KEY를 BINARY(16)에서 CHAR(36)으로 변환하는 마이그레이션
/// </summary>
public static class MigratePartyKeyToString
{
    public static async Task RunAsync(MySqlConnection connection)
    {
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        
        try
        {
            Console.WriteLine("[Migration] PARTY_KEY 변환 시작...");
            
            // 1. PARTY 테이블 변환
            await MigrateTableAsync(connection, transaction, "PARTY");
            
            // 2. PARTY_MEMBER 테이블 변환
            await MigrateTableAsync(connection, transaction, "PARTY_MEMBER");
            
            // 3. PARTY_WAIT_MEMBER 테이블 변환
            await MigrateTableAsync(connection, transaction, "PARTY_WAIT_MEMBER");
            
            await transaction.CommitAsync();
            Console.WriteLine("[Migration] PARTY_KEY 변환 완료!");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            Console.WriteLine($"[Migration] 오류 발생: {ex.Message}");
            throw;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
    
    private static async Task MigrateTableAsync(MySqlConnection connection, MySqlTransaction transaction, string tableName)
    {
        Console.WriteLine($"[Migration] {tableName} 테이블 변환 중...");
        
        // 1. 임시 컬럼 생성
        await connection.ExecuteAsync(
            $"ALTER TABLE {tableName} ADD COLUMN PARTY_KEY_TEMP CHAR(36) NULL",
            transaction: transaction);
        
        // 2. 기존 데이터 읽기 및 변환
        var rows = await connection.QueryAsync<dynamic>(
            $"SELECT PARTY_KEY FROM {tableName} WHERE PARTY_KEY IS NOT NULL",
            transaction: transaction);
        
        foreach (var row in rows)
        {
            byte[]? binaryGuid = row.PARTY_KEY as byte[];
            if (binaryGuid != null && binaryGuid.Length == 16)
            {
                // BINARY(16)을 Guid로 변환
                var guid = new Guid(binaryGuid);
                var guidString = guid.ToString();
                
                // 업데이트 (MESSAGE_KEY나 다른 고유 키를 사용하여 식별)
                // PARTY 테이블의 경우 MESSAGE_KEY 사용
                if (tableName == "PARTY")
                {
                    var messageKey = await connection.QuerySingleOrDefaultAsync<ulong>(
                        $"SELECT MESSAGE_KEY FROM {tableName} WHERE PARTY_KEY = @BinaryGuid",
                        new { BinaryGuid = binaryGuid },
                        transaction: transaction);
                    
                    if (messageKey != 0)
                    {
                        await connection.ExecuteAsync(
                            $"UPDATE {tableName} SET PARTY_KEY_TEMP = @GuidString WHERE MESSAGE_KEY = @MessageKey",
                            new { GuidString = guidString, MessageKey = messageKey },
                            transaction: transaction);
                    }
                }
                else
                {
                    // PARTY_MEMBER, PARTY_WAIT_MEMBER의 경우 PARTY_KEY로 직접 업데이트
                    await connection.ExecuteAsync(
                        $"UPDATE {tableName} SET PARTY_KEY_TEMP = @GuidString WHERE PARTY_KEY = @BinaryGuid",
                        new { GuidString = guidString, BinaryGuid = binaryGuid },
                        transaction: transaction);
                }
            }
        }
        
        // 3. 기존 컬럼 삭제
        await connection.ExecuteAsync(
            $"ALTER TABLE {tableName} DROP COLUMN PARTY_KEY",
            transaction: transaction);
        
        // 4. 임시 컬럼을 PARTY_KEY로 이름 변경
        await connection.ExecuteAsync(
            $"ALTER TABLE {tableName} CHANGE COLUMN PARTY_KEY_TEMP PARTY_KEY CHAR(36) NULL",
            transaction: transaction);
        
        Console.WriteLine($"[Migration] {tableName} 테이블 변환 완료");
    }
}

